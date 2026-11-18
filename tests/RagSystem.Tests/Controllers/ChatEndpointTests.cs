using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Google.GenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;
using RagSystem.Chat.Rules;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Tests.Database;
using RagSystem.Tests.Documents;
using RagSystem.Tests.Gemini;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Controllers;

/// <summary>The chat endpoint on a real Postgres with a fake embedding service and a fake chat model (skipped unless RAGSYSTEM_TEST_DB is set).</summary>
public class ChatEndpointTests
{
    private static float[] Direction(float x, float y)
    {
        var vector = new float[RagDbContext.EmbeddingDimensions];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    /// <summary>Every question is embedded as the direction (1, 0), so a chunk in that direction matches with score 1.</summary>
    private static readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Embedder =
        (_, _) => Task.FromResult(StubHandler.Reply(HttpStatusCode.OK, StubHandler.EmbeddingsJson([Direction(1, 0)])));

    /// <summary>
    /// Chunk i points 45 degrees away from the question, towards its own axis: cosine to the question 0.71 (kept), cosine to
    /// every other chunk 0.5 (so none is dropped as a near-duplicate).
    /// </summary>
    private static async Task AddDocumentAsync(RagDbContext db, string version = "v3", string title = "NDC Portal APIs V3", int chunks = 3)
    {
        var document = new Document { Version = version, FileName = $"{title}.md", Title = title, Status = DocumentStatus.Ready };
        db.Documents.Add(document);
        for (var i = 0; i < chunks; i++)
        {
            var vector = Direction(1, 0);
            vector[i + 2] = 1;
            db.Chunks.Add(new Chunk
            {
                DocumentId = document.Id, Version = version, Ordinal = i, Content = $"{title} text {i}", EstTokens = 10,
                Embedding = new Vector(vector), EmbeddingModel = "test",
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Postgres may change the spacing of a jsonb value, so flags are compared as JSON, not as text.</summary>
    private static Dictionary<string, string>? FlagsOf(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

    private static Task<ApiHost> HostAsync(FakeChatClient chat, Func<RagDbContext, Task>? prepare = null) =>
        ApiHost.StartAsync(Embedder, prepare ?? (db => AddDocumentAsync(db)), chat);

    private static Task<HttpResponseMessage> Ask(ApiHost host, string message, Guid? conversationId = null, string version = "v3") =>
        host.Client.PostAsJsonAsync($"/api/{version}/chat", new { conversationId, message });

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string ReasonOf(JsonElement problem) => problem.GetProperty("reason").GetString()!;

    [RequiresDatabaseFact]
    public async Task A_question_is_answered_with_citations_usage_and_a_saved_conversation()
    {
        var chat = FakeChatClient.Saying("FareCheck checks a fare.\n\nSources: S2", input: 120, output: 30, thinking: 4);
        await using var host = await HostAsync(chat);

        var response = await Ask(host, "What does FareCheck do?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("FareCheck checks a fare.", body.GetProperty("answer").GetString());
        var citation = Assert.Single(body.GetProperty("citations").EnumerateArray());
        Assert.Equal("NDC Portal APIs V3", citation.GetProperty("title").GetString());
        Assert.Equal(2, citation.GetProperty("part").GetInt32()); // S2 is the second chunk in reading order: ordinal 1, "part 2"
        Assert.Equal("NDC Portal APIs V3, part 2", citation.GetProperty("text").GetString());
        var usage = body.GetProperty("usage");
        Assert.Equal((120, 30, 4, false), (usage.GetProperty("inputTokens").GetInt32(), usage.GetProperty("outputTokens").GetInt32(),
            usage.GetProperty("thinkingTokens").GetInt32(), usage.GetProperty("cacheHit").GetBoolean()));

        // The model got the rules and one user message with the documentation and the question.
        var (sent, options) = Assert.Single(chat.Calls);
        Assert.Equal(["System", "User"], sent.Select(m => m.Role.Value switch { "system" => "System", _ => "User" }));
        Assert.Contains("<source id=\"S1\">", sent[1].Text);
        Assert.EndsWith("Question: What does FareCheck do?", sent[1].Text);
        Assert.Equal(3000, options!.MaxOutputTokens);

        // The exchange is saved: a question and an answer.
        await using var db = host.Context();
        var conversation = await db.Conversations.SingleAsync();
        Assert.Equal(conversation.Id, body.GetProperty("conversationId").GetGuid());
        Assert.Equal("v3", conversation.Version);
        var messages = await db.Messages.OrderBy(m => m.CreatedAt).ThenByDescending(m => m.Role).ToListAsync();
        Assert.Equal([MessageRoles.User, MessageRoles.Assistant], messages.Select(m => m.Role));
        Assert.Equal("What does FareCheck do?", messages[0].Content);
        Assert.Equal("What does FareCheck do?", messages[0].RetrievalQuery);
        Assert.Equal("FareCheck checks a fare.", messages[1].Content);
        Assert.Equal(body.GetProperty("messageId").GetGuid(), messages[1].Id);
        Assert.Equal((120, 30, 4), (messages[1].InputTokens, messages[1].OutputTokens, messages[1].ThinkingTokens));
        Assert.Single(messages[1].CitedChunkIds);
        Assert.Null(messages[1].GuardFlags);
    }

    [RequiresDatabaseFact]
    public async Task Nothing_relevant_is_not_covered_with_no_model_call_and_no_tokens()
    {
        var chat = FakeChatClient.Saying("must not be asked");
        await using var host = await HostAsync(chat, prepare: _ => Task.CompletedTask); // no documents

        var response = await Ask(host, "Tell me a joke");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(PromptRules.NotCovered("v3"), body.GetProperty("answer").GetString());
        Assert.Empty(body.GetProperty("citations").EnumerateArray());
        Assert.Equal(0, body.GetProperty("usage").GetProperty("inputTokens").GetInt32());
        Assert.Empty(chat.Calls);

        await using var db = host.Context();
        Assert.Equal(2, await db.Messages.CountAsync()); // it is still a turn of the conversation
    }

    [RequiresDatabaseFact]
    public async Task Sources_none_from_the_model_gives_its_text_and_no_citations()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("The documentation does not cover that.\nSources: none"));

        var body = await JsonOf(await Ask(host, "What is the weather?"));

        Assert.Equal("The documentation does not cover that.", body.GetProperty("answer").GetString());
        Assert.Empty(body.GetProperty("citations").EnumerateArray());
    }

    [RequiresDatabaseFact]
    public async Task A_reply_with_no_sources_line_cites_the_first_three_and_flags_the_saved_answer()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("An answer without a sources line."), db => AddDocumentAsync(db, chunks: 5));

        var body = await JsonOf(await Ask(host, "What is FareCheck?"));

        Assert.Equal(3, body.GetProperty("citations").GetArrayLength());
        await using var db = host.Context();
        var answer = await db.Messages.SingleAsync(m => m.Role == MessageRoles.Assistant);
        Assert.Equal(new Dictionary<string, string> { ["sources_missing"] = "true" }, FlagsOf(answer.GuardFlags));
        Assert.Equal(3, answer.CitedChunkIds.Length);
    }

    [RequiresDatabaseFact]
    public async Task A_follow_up_is_rewritten_searched_with_the_rewrite_and_answered_with_the_history()
    {
        var calls = 0;
        var chat = new FakeChatClient((_, _, _) =>
        {
            var text = ++calls switch
            {
                1 => "FareCheck checks a fare.\nSources: S1",   // answer to the first question
                2 => "What does the FareCheck response contain?", // the rewrite
                _ => "It contains the fare.\nSources: S1",      // answer to the follow-up
            };
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        });
        await using var host = await HostAsync(chat);
        var first = await JsonOf(await Ask(host, "What does FareCheck do?"));
        var conversationId = first.GetProperty("conversationId").GetGuid();

        var second = await Ask(host, "and what does its response contain?", conversationId);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await JsonOf(second);
        Assert.Equal(conversationId, body.GetProperty("conversationId").GetGuid()); // the same conversation
        Assert.Equal("It contains the fare.", body.GetProperty("answer").GetString());

        Assert.Equal(3, chat.Calls.Count);
        Assert.Contains("Last message: and what does its response contain?", chat.Calls[1].Messages[1].Text); // the rewrite call
        var (answerMessages, _) = chat.Calls[2];
        Assert.Equal(["What does FareCheck do?", "FareCheck checks a fare."], answerMessages.Skip(1).Take(2).Select(m => m.Text)); // the history
        Assert.EndsWith(
            "Question: and what does its response contain?\n(The same question made standalone from the conversation: What does the FareCheck response contain?)",
            answerMessages[^1].Text);

        await using var db = host.Context();
        var question = await db.Messages.Where(m => m.Role == MessageRoles.User).OrderByDescending(m => m.CreatedAt).FirstAsync();
        Assert.Equal("and what does its response contain?", question.Content);
        Assert.Equal("What does the FareCheck response contain?", question.RetrievalQuery);
        Assert.Null(question.GuardFlags);
        Assert.Single(await db.Conversations.ToListAsync());
        Assert.Equal(4, await db.Messages.CountAsync());
    }

    [RequiresDatabaseFact]
    public async Task A_failed_rewrite_falls_back_to_the_two_questions_and_the_reason_is_saved()
    {
        var calls = 0;
        var chat = new FakeChatClient((_, _, _) =>
            ++calls == 2
                ? throw new ApiException("quota", 429, "RESOURCE_EXHAUSTED", null!)
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "An answer.\nSources: S1"))));
        await using var host = await HostAsync(chat);
        var first = await JsonOf(await Ask(host, "What does FareCheck do?"));

        var second = await Ask(host, "and its response?", first.GetProperty("conversationId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // the answer still works
        Assert.DoesNotContain("standalone", chat.Calls[2].Messages[^1].Text); // a fallback is not offered as standalone
        await using var db = host.Context();
        var question = await db.Messages.Where(m => m.Role == MessageRoles.User).OrderByDescending(m => m.CreatedAt).FirstAsync();
        Assert.Equal("What does FareCheck do? and its response?", question.RetrievalQuery);
        Assert.Equal(new Dictionary<string, string> { ["rewrite"] = "rate_limited" }, FlagsOf(question.GuardFlags));
    }

    [RequiresDatabaseFact]
    public async Task A_conversation_that_does_not_exist_is_404_and_the_model_is_not_called()
    {
        var chat = FakeChatClient.Saying("no");
        await using var host = await HostAsync(chat);

        var response = await Ask(host, "hello", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("conversation_not_found", ReasonOf(await JsonOf(response)));
        Assert.Empty(chat.Calls);
    }

    [RequiresDatabaseFact]
    public async Task A_conversation_of_another_version_looks_like_one_that_does_not_exist()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("An answer.\nSources: S1"),
            async db => { await AddDocumentAsync(db, "v3"); await AddDocumentAsync(db, "v2", "NDC Portal APIs V2"); });
        var v3 = await JsonOf(await Ask(host, "What is FareCheck?", version: "v3"));

        var response = await Ask(host, "and in v2?", v3.GetProperty("conversationId").GetGuid(), version: "v2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("conversation_not_found", ReasonOf(await JsonOf(response)));
    }

    [RequiresDatabaseTheory]
    [InlineData("/api/v4/chat", "{\"message\":\"hello\"}", "unknown_version")]
    [InlineData("/api/v3/chat", "{\"message\":\"\"}", "empty_question")]
    [InlineData("/api/v3/chat", "{\"message\":\"   \"}", "empty_question")]
    [InlineData("/api/v3/chat", "{}", "empty_question")]
    public async Task A_bad_request_is_a_400_problem_with_its_reason_and_nothing_is_called(string url, string json, string reason)
    {
        var chat = FakeChatClient.Saying("no");
        await using var host = await HostAsync(chat);

        var response = await host.Client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(reason, ReasonOf(await JsonOf(response)));
        Assert.Empty(chat.Calls);
        await using var db = host.Context();
        Assert.Empty(await db.Conversations.ToListAsync());
    }

    [RequiresDatabaseFact]
    public async Task A_message_over_1000_tokens_is_400_question_too_long()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("no"));

        var response = await Ask(host, new string('a', 3600));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("question_too_long", ReasonOf(await JsonOf(response)));
    }

    [RequiresDatabaseTheory]
    [InlineData(429, 503, "llm_unavailable")]
    [InlineData(503, 503, "llm_unavailable")]
    [InlineData(401, 502, "llm_error")]
    [InlineData(404, 502, "llm_error")]
    public async Task A_model_failure_is_503_or_502_with_its_reason_and_nothing_is_saved(int geminiStatus, int expected, string reason)
    {
        await using var host = await HostAsync(FakeChatClient.Failing(new ApiException("failed", geminiStatus, "STATUS", null!)));

        var response = await Ask(host, "What is FareCheck?");

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(reason, ReasonOf(await JsonOf(response)));
        if (expected == 503)
            Assert.Equal("30", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        await using var db = host.Context();
        Assert.Empty(await db.Conversations.ToListAsync()); // a failed request leaves nothing behind
        Assert.Empty(await db.Messages.ToListAsync());
    }

    [RequiresDatabaseFact]
    public async Task An_empty_model_answer_is_502_empty_answer_and_nothing_is_saved()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("Sources: S1"));

        var response = await Ask(host, "What is FareCheck?");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("empty_answer", ReasonOf(await JsonOf(response)));
        await using var db = host.Context();
        Assert.Empty(await db.Messages.ToListAsync());
    }

    [RequiresDatabaseFact]
    public async Task The_embedding_service_being_down_is_503_before_any_chat_call()
    {
        var chat = FakeChatClient.Saying("no");
        await using var host = await ApiHost.StartAsync(
            (_, _) => Task.FromResult(StubHandler.Reply(HttpStatusCode.ServiceUnavailable, "{\"error\":\"loading\"}")),
            db => AddDocumentAsync(db), chat);

        var response = await Ask(host, "What is FareCheck?");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("llm_unavailable", ReasonOf(await JsonOf(response)));
        Assert.Empty(chat.Calls);
    }

    [RequiresDatabaseFact]
    public async Task The_history_of_a_conversation_is_listed_with_citations_and_usage()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("An answer.\nSources: S3", input: 50, output: 7));
        var answered = await JsonOf(await Ask(host, "What is FareCheck?"));
        var id = answered.GetProperty("conversationId").GetGuid();

        var response = await host.Client.GetAsync($"/api/V3/conversations/{id}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = (await JsonOf(response)).EnumerateArray().ToList();
        Assert.Equal(["user", "assistant"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("What is FareCheck?", messages[0].GetProperty("content").GetString());
        Assert.Empty(messages[0].GetProperty("citations").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, messages[0].GetProperty("usage").ValueKind);
        Assert.Equal("An answer.", messages[1].GetProperty("content").GetString());
        Assert.Equal("NDC Portal APIs V3, part 3", Assert.Single(messages[1].GetProperty("citations").EnumerateArray()).GetProperty("text").GetString());
        Assert.Equal(50, messages[1].GetProperty("usage").GetProperty("inputTokens").GetInt32());
        Assert.Equal(answered.GetProperty("messageId").GetGuid(), messages[1].GetProperty("id").GetGuid());
    }

    [RequiresDatabaseFact]
    public async Task Listing_the_messages_of_an_unknown_conversation_or_of_another_version_is_404_and_a_bad_version_is_400()
    {
        await using var host = await HostAsync(FakeChatClient.Saying("An answer.\nSources: S1"));
        var id = (await JsonOf(await Ask(host, "What is FareCheck?"))).GetProperty("conversationId").GetGuid();

        var unknown = await host.Client.GetAsync($"/api/v3/conversations/{Guid.NewGuid()}/messages");
        var otherVersion = await host.Client.GetAsync($"/api/v2/conversations/{id}/messages");
        var badVersion = await host.Client.GetAsync($"/api/v9/conversations/{id}/messages");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("conversation_not_found", ReasonOf(await JsonOf(unknown)));
        Assert.Equal(HttpStatusCode.NotFound, otherVersion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);
        Assert.Equal("unknown_version", ReasonOf(await JsonOf(badVersion)));
    }
}
