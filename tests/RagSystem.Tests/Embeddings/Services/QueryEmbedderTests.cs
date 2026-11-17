using System.Net;
using System.Text.Json;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Embeddings.Services;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Embeddings.Services;

public class QueryEmbedderTests
{
    private static QueryEmbedder EmbedderFor(StubHandler handler) =>
        new(OllamaTestGenerators.For(handler));

    private static StubHandler Embeds(params float[][] vectors) =>
        StubHandler.Json(HttpStatusCode.OK, StubHandler.EmbeddingsJson(vectors));

    [Fact]
    public async Task The_question_is_sent_exactly_as_it_was_written_with_no_instruction()
    {
        var handler = Embeds(StubHandler.Vector(1));

        await EmbedderFor(handler).EmbedAsync("What does error 409 mean?");

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var sent = Assert.Single(body.RootElement.GetProperty("input").EnumerateArray()).GetString();
        Assert.Equal("What does error 409 mean?", sent);
    }

    [Fact]
    public async Task One_call_is_made_and_the_vector_is_scaled_to_length_1()
    {
        var handler = Embeds(StubHandler.Vector(5f));

        var vector = await EmbedderFor(handler).EmbedAsync("hello");

        Assert.Single(handler.Requests);
        Assert.Equal(1024, vector.Length);
        Assert.Equal(1.0, Math.Sqrt(vector.Sum(x => (double)x * x)), precision: 6);
        Assert.Equal(1f, vector[0]);
    }

    [Fact]
    public async Task A_vector_of_the_wrong_length_is_a_fatal_error()
    {
        var error = await Assert.ThrowsAsync<EmbeddingException>(
            () => EmbedderFor(Embeds(StubHandler.Vector(1, length: 768))).EmbedAsync("hello"));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
    }

    [Fact]
    public async Task A_failure_goes_to_the_caller_without_a_retry()
    {
        var handler = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"loading\"}");

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => EmbedderFor(handler).EmbedAsync("hello"));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.Single(handler.Requests);
    }
}
