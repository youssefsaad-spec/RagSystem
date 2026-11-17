using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Tests.Database;
using RagSystem.Tests.Documents;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Controllers;

/// <summary>The search endpoint on a real Postgres with a fake Ollama (skipped unless RAGSYSTEM_TEST_DB is set).</summary>
public class SearchEndpointTests
{
    /// <summary>A vector made of two numbers (the rest 0); its direction decides its cosine to the question.</summary>
    private static float[] Direction(float x, float y)
    {
        var vector = new float[RagDbContext.EmbeddingDimensions];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    /// <summary>A fake Ollama that embeds every text as this vector and remembers what it was sent.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OllamaAnswering(float[] vector, List<string>? sent = null) =>
        async (request, _) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            sent?.AddRange(body.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()!));
            return StubHandler.Reply(HttpStatusCode.OK, StubHandler.EmbeddingsJson([vector]));
        };

    private static async Task AddDocumentAsync(RagDbContext db, string version, string title, string fileName, params (int Ordinal, float[] Embedding)[] chunks)
    {
        var document = new Document { Version = version, FileName = fileName, Title = title, Status = DocumentStatus.Ready };
        db.Documents.Add(document);
        foreach (var (ordinal, embedding) in chunks)
            db.Chunks.Add(new Chunk
            {
                DocumentId = document.Id, Version = version, Ordinal = ordinal, Content = $"{title} text {ordinal}", EstTokens = 10,
                Embedding = new Vector(embedding), EmbeddingModel = "qwen3-embedding:0.6b",
            });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [RequiresDatabaseFact]
    public async Task A_question_returns_the_kept_sources_in_reading_order_and_the_dropped_ones_with_reasons()
    {
        var sent = new List<string>();
        await using var host = await ApiHost.StartAsync(
            OllamaAnswering(Direction(1, 0), sent),
            db => AddDocumentAsync(db, "v3", "NDC Portal APIs V3", "guide.md",
                (0, Direction(0, 1)),     // cosine 0: below the minimum score
                (1, Direction(1, 1)),     // cosine 0.707
                (2, Direction(1, 0))));   // cosine 1

        var response = await host.Client.GetAsync("/api/v3/search?q=What%20does%20error%20409%20mean%3F");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("v3", body.GetProperty("version").GetString());
        Assert.Equal("What does error 409 mean?", body.GetProperty("question").GetString());
        Assert.Equal(3, body.GetProperty("candidates").GetInt32());

        // Best match first in score, but the model reads the document in order: part 2 (S1) before part 3 (S2).
        var sources = body.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(["S1", "S2"], sources.Select(s => s.GetProperty("label").GetString()));
        Assert.Equal(["NDC Portal APIs V3, part 2", "NDC Portal APIs V3, part 3"], sources.Select(s => s.GetProperty("citation").GetString()));
        Assert.Equal("NDC Portal APIs V3 text 1", sources[0].GetProperty("content").GetString());
        Assert.Equal(Math.Sqrt(0.5), sources[0].GetProperty("score").GetDouble(), precision: 4);
        Assert.Equal(1.0, sources[1].GetProperty("score").GetDouble(), precision: 4);

        var dropped = Assert.Single(body.GetProperty("dropped").EnumerateArray());
        Assert.Equal("NDC Portal APIs V3, part 1", dropped.GetProperty("citation").GetString());
        Assert.Equal("BelowMinScore", dropped.GetProperty("reason").GetString());

        // The question is embedded exactly as written.
        Assert.Equal(["What does error 409 mean?"], sent);
    }

    [RequiresDatabaseFact]
    public async Task Only_the_chunks_of_the_asked_version_are_searched_and_the_version_may_be_any_case()
    {
        await using var host = await ApiHost.StartAsync(
            OllamaAnswering(Direction(1, 0)),
            async db =>
            {
                await AddDocumentAsync(db, "v2", "V2 guide", "v2.md", (0, Direction(1, 0)));
                await AddDocumentAsync(db, "v3", "V3 guide", "v3.md", (0, Direction(1, 0)));
            });

        var body = await JsonOf(await host.Client.GetAsync("/api/V3/search?q=hello"));

        Assert.Equal("v3", body.GetProperty("version").GetString());
        var source = Assert.Single(body.GetProperty("sources").EnumerateArray());
        Assert.Equal("V3 guide, part 1", source.GetProperty("citation").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_version_with_no_documents_gives_200_with_no_sources()
    {
        await using var host = await ApiHost.StartAsync(OllamaAnswering(Direction(1, 0)));

        var response = await host.Client.GetAsync("/api/v3/search?q=hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(0, body.GetProperty("candidates").GetInt32());
        Assert.Empty(body.GetProperty("sources").EnumerateArray());
    }

    [RequiresDatabaseTheory]
    [InlineData("/api/v4/search?q=hello", "unknown_version")]
    [InlineData("/api/v3/search", "empty_question")]
    [InlineData("/api/v3/search?q=%20%20", "empty_question")]
    public async Task A_bad_request_is_a_400_problem_with_its_reason_and_ollama_is_not_called(string url, string reason)
    {
        var sent = new List<string>();
        await using var host = await ApiHost.StartAsync(OllamaAnswering(Direction(1, 0), sent));

        var response = await host.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(reason, (await JsonOf(response)).GetProperty("reason").GetString());
        Assert.Empty(sent);
    }

    [RequiresDatabaseFact]
    public async Task A_question_over_1000_tokens_is_400_question_too_long()
    {
        await using var host = await ApiHost.StartAsync(OllamaAnswering(Direction(1, 0)));

        var response = await host.Client.GetAsync("/api/v3/search?q=" + new string('a', 3600));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("question_too_long", (await JsonOf(response)).GetProperty("reason").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Ollama_being_down_is_503_llm_unavailable_with_a_retry_after()
    {
        await using var host = await ApiHost.StartAsync(
            (_, _) => Task.FromResult(StubHandler.Reply(HttpStatusCode.ServiceUnavailable, "{\"error\":\"loading\"}")));

        var response = await host.Client.GetAsync("/api/v3/search?q=hello");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.Equal("llm_unavailable", (await JsonOf(response)).GetProperty("reason").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_model_that_is_not_pulled_is_502_llm_error()
    {
        await using var host = await ApiHost.StartAsync(
            (_, _) => Task.FromResult(StubHandler.Reply(HttpStatusCode.NotFound, "{\"error\":\"model not found, try pulling it first\"}")));

        var response = await host.Client.GetAsync("/api/v3/search?q=hello");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("llm_error", (await JsonOf(response)).GetProperty("reason").GetString());
    }
}
