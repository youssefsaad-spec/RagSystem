using System.Text;
using Microsoft.Extensions.AI;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Chat.Settings;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Gemini.Models;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;
using RagSystem.Observability;

namespace RagSystem.Chat.Services;

/// <summary>
/// Turns a follow-up question into a standalone one before retrieval (F27). The first message of a conversation is used as it
/// is, with no model call. If the rewrite fails or is unusable, the previous question and the current one are joined instead.
/// </summary>
public sealed class QueryRewriter(GeminiChat chat, GeminiSettings gemini, ChatSettings settings)
{
    /// <summary>The model's rewrite is invalid when it is longer than this (characters).</summary>
    public const int MaxQueryChars = 300;

    /// <summary>Earlier answers are cut to this many characters before they go to the rewrite.</summary>
    public const int AnswerChars = 400;

    private static readonly string SystemPrompt = string.Join("\n",
        "You rewrite the last message of a conversation about the NDC Portal APIs into one standalone question.",
        "Rules:",
        "- Return exactly one question and nothing else.",
        "- Write it in the user's language.",
        "- Keep endpoint, field and enum names exactly as written.",
        "- Do not answer the question.",
        "- If the message is already a standalone question, return it unchanged.");

    private const string Prefix = "Standalone question:";

    /// <param name="history">Earlier question-and-answer pairs, oldest first. Empty for the first message.</param>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public Task<RewriteResult> RewriteAsync(IReadOnlyList<HistoryTurn> history, string message, CancellationToken cancellationToken) =>
        Telemetry.TraceAsync("rewrite_query", Telemetry.Kinds.Chain, async span =>
        {
            span.SetInput(message);
            span.SetMeta("history_turns", history.Count);
            var result = await RewriteCoreAsync(history, message, cancellationToken);
            span.SetOutput(result.Query);
            span.SetMeta("skipped", history.Count == 0); // the first message is used as it is, with no model call
            span.SetMeta("is_standalone", result.IsStandalone);
            span.SetMeta("changed", result.Query != message);
            span.SetMeta("fallback_reason", result.FallbackReason); // why the model's rewrite was not used (null = it was, or not needed)
            return result;
        });

    private async Task<RewriteResult> RewriteCoreAsync(IReadOnlyList<HistoryTurn> history, string message, CancellationToken cancellationToken)
    {
        if (history.Count == 0)
            return new RewriteResult(message, IsStandalone: true, FallbackReason: null);

        var recent = history.Skip(Math.Max(0, history.Count - settings.RewriteHistoryTurns)).ToList();
        var request = new ModelRequest(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, Conversation(recent, message))],
            gemini.RewriteModel,
            settings.RewriteMaxOutputTokens,
            TimeSpan.FromSeconds(settings.RewriteTimeoutSeconds));

        try
        {
            var reply = await chat.GenerateAsync(request, cancellationToken);
            var query = Clean(reply.Text);
            return query is null
                ? Fallback(history, message, RewriteReasons.InvalidOutput)
                : new RewriteResult(query, IsStandalone: true, FallbackReason: null);
        }
        catch (EmbeddingException error)
        {
            return Fallback(history, message, ReasonOf(error));
        }
    }

    private static string ReasonOf(EmbeddingException error) => error switch
    {
        { Kind: EmbeddingFailureKind.RateLimited } => RewriteReasons.RateLimited,
        { InnerException: OperationCanceledException } => RewriteReasons.Timeout,
        _ => RewriteReasons.Error,
    };

    /// <summary>The previous question and the current one. It is not standalone, so the cache (F35) must not use it.</summary>
    private static RewriteResult Fallback(IReadOnlyList<HistoryTurn> history, string message, string reason) =>
        new($"{history[^1].Question} {message}", IsStandalone: false, reason);

    private static string Conversation(IReadOnlyList<HistoryTurn> turns, string message)
    {
        var text = new StringBuilder("Conversation so far:\n");
        foreach (var turn in turns)
        {
            var answer = turn.Answer.Length > AnswerChars ? turn.Answer[..AnswerChars] + "..." : turn.Answer;
            text.Append("User: ").Append(turn.Question).Append('\n').Append("Assistant: ").Append(answer).Append('\n');
        }

        return text.Append("\nLast message: ").Append(message).ToString();
    }

    /// <summary>The first non-empty line, without quotes or a leading "Standalone question:". Null when it is empty or too long.</summary>
    internal static string? Clean(string reply)
    {
        var line = reply.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null)
            return null;

        line = line.Trim('*', '_').Trim();
        if (line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            line = line[Prefix.Length..].Trim();
        line = line.Trim('"', '\'', '“', '”', '‘', '’', '`', '*', '_').Trim();

        return line.Length is 0 or > MaxQueryChars ? null : line;
    }
}
