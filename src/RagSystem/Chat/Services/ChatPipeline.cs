using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RagSystem.Chat.Exceptions;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Chat.Settings;
using RagSystem.Gemini.Models;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;
using RagSystem.Observability;
using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Services;

namespace RagSystem.Chat.Services;

/// <summary>
/// One chat turn as small steps (F25): load the history, rewrite a follow-up question, retrieve, and either answer "not
/// covered" without calling the model, or build the prompt, call the model and read its sources. Then the exchange is saved.
/// The guards, the cache and the limits (F29 to F35) are added around these steps by their own features.
/// </summary>
public sealed class ChatPipeline(
    ConversationStore store,
    QueryRewriter rewriter,
    Retriever retriever,
    PromptBuilder prompts,
    GeminiChat chat,
    GeminiSettings gemini,
    ChatSettings settings,
    ILogger<ChatPipeline> logger)
{
    /// <param name="conversationId">The conversation to continue, or null to start one.</param>
    /// <param name="clientId">The client that asks. Null until F29.</param>
    /// <exception cref="ConversationNotFoundException">The conversation is not this client's, or not in this version.</exception>
    /// <exception cref="RagSystem.Embeddings.Exceptions.EmbeddingException">A model call failed (embedding or chat).</exception>
    /// <exception cref="EmptyAnswerException">The model answered nothing.</exception>
    public async Task<ChatResult> AskAsync(string version, string message, Guid? conversationId, Guid? clientId, CancellationToken ct)
    {
        // The root span of the trace in LangSmith: one chat turn, with the question in and the answer out. Every step below is a child.
        using var turnSpan = Telemetry.StartRoot("chat_turn", Telemetry.Kinds.Chain);
        turnSpan.SetInput(message);
        turnSpan.SetMeta("version", version);
        turnSpan.SetMeta("conversation_id", conversationId); // replaced below by the saved conversation's id (also for a new one)

        try
        {
            var result = await AskCoreAsync(version, message, conversationId, clientId, turnSpan, ct);
            turnSpan.SetOutput(result.Answer);
            turnSpan.SetMeta("conversation_id", result.ConversationId); // LangSmith groups traces with this key into one thread
            turnSpan.SetMeta("message_id", result.MessageId);
            turnSpan.SetMeta("citations", result.Citations.Count);
            turnSpan.SetMeta("input_tokens", result.InputTokens);
            turnSpan.SetMeta("output_tokens", result.OutputTokens);
            turnSpan.SetMeta("thinking_tokens", result.ThinkingTokens);
            turnSpan.SetMeta("cache_hit", result.CacheHit);
            return result;
        }
        catch (Exception error)
        {
            turnSpan.Fail(error);
            throw;
        }
    }

    private async Task<ChatResult> AskCoreAsync(
        string version, string message, Guid? conversationId, Guid? clientId, Activity? turnSpan, CancellationToken ct)
    {
        var history = conversationId is { } id
            ? await Telemetry.TraceAsync("load_history", Telemetry.Kinds.Chain, async span =>
            {
                var turns = await store.LoadHistoryAsync(id, version, clientId, ct);
                span.SetMeta("turns", turns.Count);
                return turns;
            })
            : [];

        var rewrite = await rewriter.RewriteAsync(history, message, ct);
        var retrieval = await retriever.RetrieveAsync(version, rewrite.Query, ct);

        var userFlags = rewrite.FallbackReason is { } reason ? new Dictionary<string, string> { ["rewrite"] = reason } : null;

        if (retrieval.IsEmpty)
        {
            // Nothing relevant: no model call, no tokens.
            turnSpan.SetMeta("outcome", "not_covered");
            var notCovered = new NewTurn(message, rewrite.Query, userFlags, PromptRules.NotCovered(version), null, null, 0, 0, 0, [], null);
            return await SaveAsync(version, conversationId, clientId, notCovered, [], ct);
        }

        var standalone = rewrite.IsStandalone && rewrite.Query != message ? rewrite.Query : null;
        var messages = Telemetry.Trace("build_prompt", Telemetry.Kinds.Prompt, span =>
        {
            span.SetMeta("messages", history.Count * 2 + 2);
            span.SetMeta("history_turns", history.Count);
            span.SetMeta("sources", string.Join(",", retrieval.Sources.Select(s => s.Label)));
            span.SetMeta("used_standalone_question", standalone is not null);
            return prompts.Build(version, message, standalone, retrieval.Sources, history);
        });

        try
        {
            var reply = await chat.GenerateAsync(
            new ModelRequest(messages, gemini.ChatModel, gemini.MaxOutputTokens, TimeSpan.FromSeconds(settings.ChatTimeoutSeconds)), ct);

            var parsed = Telemetry.Trace("parse_answer", Telemetry.Kinds.Parser, span =>
            {
                span.SetInput(reply.Text);
                var result = AnswerParser.Parse(reply.Text, retrieval.Sources);
                span.SetOutput(result.Text);
                span.SetMeta("cited", string.Join(",", result.Cited.Select(c => c.Label)));
                span.SetMeta("sources_missing", result.SourcesMissing);
                span.SetMeta("empty", result.IsEmpty);
                return result;
            });
            if (parsed.IsEmpty)
                throw new EmptyAnswerException();

            var flags = new Dictionary<string, string>();
            if (parsed.SourcesMissing)
                flags["sources_missing"] = "true";
            if (reply.Truncated)
                flags["truncated"] = "true";
            turnSpan.SetMeta("outcome", "answered");
            turnSpan.SetMeta("truncated", reply.Truncated);
            turnSpan.SetMeta("sources_missing", parsed.SourcesMissing);
            if (parsed.SourcesMissing || reply.Truncated)
                logger.LogWarning("chat: answer is not clean (sources missing: {SourcesMissing}, truncated: {Truncated}).", parsed.SourcesMissing, reply.Truncated);

            var turn = new NewTurn(
                message, rewrite.Query, userFlags, parsed.Text, gemini.ChatModel, reply.Model,
                reply.InputTokens, reply.OutputTokens, reply.ThinkingTokens,
                parsed.Cited.Select(s => s.ChunkId).ToList(), flags.Count > 0 ? flags : null);
            return await SaveAsync(version, conversationId, clientId, turn, parsed.Cited, ct, reply);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    private async Task<ChatResult> SaveAsync(
        string version, Guid? conversationId, Guid? clientId, NewTurn turn,
        IReadOnlyList<RetrievedSource> cited, CancellationToken ct, ModelReply? reply = null)
    {
        var (savedConversation, answerId) = await Telemetry.TraceAsync("save_turn", Telemetry.Kinds.Tool, _ => store.SaveTurnAsync(conversationId, version, clientId, turn, ct));
        return new ChatResult(
            savedConversation, answerId, turn.Answer, cited,
            reply?.InputTokens ?? 0, reply?.OutputTokens ?? 0, reply?.ThinkingTokens ?? 0, CacheHit: false);
    }
}
