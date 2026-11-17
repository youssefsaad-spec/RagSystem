using System.Net;
using System.Text.Json;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Ollama.Services;
using RagSystem.Ollama.Settings;

namespace RagSystem.Tests.Ollama.Services;

public class OllamaEmbeddingClientTests
{
    private static OllamaEmbeddingClient ClientFor(StubHandler handler, OllamaSettings? settings = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") }, settings ?? new OllamaSettings());

    private static StubHandler Embeds(params float[][] vectors) =>
        StubHandler.Json(HttpStatusCode.OK, StubHandler.EmbeddingsJson(vectors));

    private static async Task<EmbeddingException> FailureOf(StubHandler handler, int texts = 1)
    {
        var client = ClientFor(handler);
        return await Assert.ThrowsAsync<EmbeddingException>(() => client.EmbedAsync(Enumerable.Repeat("text", texts).ToList()));
    }

    // ---- The request ----

    [Fact]
    public async Task It_posts_the_model_the_texts_and_the_dimensions_to_api_embed()
    {
        var handler = Embeds(StubHandler.Vector(1), StubHandler.Vector(2));

        await ClientFor(handler).EmbedAsync(["first", "second"]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "/api/embed"), (request.Method, request.Path));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("qwen3-embedding:0.6b", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(["first", "second"], body.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1024, body.RootElement.GetProperty("dimensions").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task EmbeddingOnCpu_adds_num_gpu_0()
    {
        var handler = Embeds(StubHandler.Vector(1));

        await ClientFor(handler, new OllamaSettings { EmbeddingOnCpu = true }).EmbedAsync(["a"]);

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(0, body.RootElement.GetProperty("options").GetProperty("num_gpu").GetInt32());
    }

    [Fact]
    public async Task The_configured_model_is_sent()
    {
        var handler = Embeds(StubHandler.Vector(1));

        await ClientFor(handler, new OllamaSettings { EmbeddingModel = "other-model" }).EmbedAsync(["a"]);

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("other-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task No_texts_means_no_call_and_no_vectors()
    {
        var handler = Embeds();

        var vectors = await ClientFor(handler).EmbedAsync([]);

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests);
    }

    // ---- The answer ----

    [Fact]
    public async Task The_vectors_come_back_in_order_exactly_as_ollama_sent_them()
    {
        var vectors = await ClientFor(Embeds(StubHandler.Vector(0.25f), StubHandler.Vector(7f))).EmbedAsync(["a", "b"]);

        Assert.Equal(2, vectors.Length);
        Assert.Equal(0.25f, vectors[0][0]);
        Assert.Equal(7f, vectors[1][0]);
        Assert.All(vectors, v => Assert.Equal(1024, v.Length));
    }

    [Fact]
    public async Task A_vector_that_is_not_1024_long_is_a_fatal_error()
    {
        var error = await FailureOf(Embeds(StubHandler.Vector(1, length: 768)));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("1024", error.Message);
        Assert.Contains("768", error.Message);
    }

    [Fact]
    public async Task A_different_number_of_vectors_than_texts_is_a_fatal_error()
    {
        var error = await FailureOf(Embeds(StubHandler.Vector(1)), texts: 2);

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("1 embeddings for 2 texts", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"embeddings\":null}")]
    public async Task An_answer_without_embeddings_is_a_fatal_error(string json)
    {
        var error = await FailureOf(StubHandler.Json(HttpStatusCode.OK, json));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_a_fatal_error()
    {
        var error = await FailureOf(StubHandler.Json(HttpStatusCode.OK, "<html>proxy page</html>"));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
    }

    // ---- Failures ----

    [Fact]
    public async Task A_model_that_is_not_pulled_is_fatal_and_the_message_says_so()
    {
        var error = await FailureOf(StubHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"model \\\"qwen3-embedding:0.6b\\\" not found, try pulling it first\"}"));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("404", error.Message);
        Assert.Contains("not found", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Other_client_errors_are_fatal(HttpStatusCode status)
    {
        var error = await FailureOf(StubHandler.Json(status, "{\"error\":\"nope\"}"));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited_and_carries_the_retry_after_seconds()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = StubHandler.Reply(HttpStatusCode.TooManyRequests, "{\"error\":\"slow down\"}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });

        var error = await FailureOf(handler);

        Assert.Equal(EmbeddingFailureKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task Too_many_requests_without_a_retry_after_has_no_wait()
    {
        var error = await FailureOf(StubHandler.Json(HttpStatusCode.TooManyRequests, "{}"));

        Assert.Equal(EmbeddingFailureKind.RateLimited, error.Kind);
        Assert.Null(error.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Server_errors_are_transient(HttpStatusCode status)
    {
        var error = await FailureOf(StubHandler.Json(status, "{\"error\":\"busy\"}"));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Fact]
    public async Task A_refused_connection_is_transient()
    {
        var error = await FailureOf(StubHandler.Throws(new HttpRequestException("Connection refused")));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Fact]
    public async Task A_timeout_is_transient()
    {
        // HttpClient reports its own timeout as a cancellation that the caller did not ask for.
        var error = await FailureOf(StubHandler.Throws(new TaskCanceledException("timed out")));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task When_the_caller_cancels_it_is_a_cancellation_and_not_an_ollama_failure()
    {
        using var source = new CancellationTokenSource();
        var handler = new StubHandler(async (_, token) =>
        {
            await source.CancelAsync();
            token.ThrowIfCancellationRequested();
            return StubHandler.Reply(HttpStatusCode.OK, "{}");
        });
        var client = ClientFor(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EmbedAsync(["a"], source.Token));
    }

    // ---- Settings ----

    [Fact]
    public void The_default_settings_are_valid_and_use_qwen3_with_1024_dimensions()
    {
        var settings = new OllamaSettings().EnsureValid();

        Assert.Equal("qwen3-embedding:0.6b", settings.EmbeddingModel);
        Assert.Equal(1024, settings.EmbeddingDimensions);
    }

    [Theory]
    [InlineData(768)]
    [InlineData(0)]
    [InlineData(2048)]
    public void A_dimension_other_than_the_database_column_is_refused(int dimensions)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new OllamaSettings { EmbeddingDimensions = dimensions }.EnsureValid());

        Assert.Contains("1024", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost:11434")]
    [InlineData("ftp://ollama")]
    public void A_base_url_that_is_not_http_is_refused(string url)
    {
        Assert.Throws<InvalidOperationException>(() => new OllamaSettings { BaseUrl = url }.EnsureValid());
    }

    [Fact]
    public void An_empty_model_and_a_zero_timeout_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => new OllamaSettings { EmbeddingModel = " " }.EnsureValid());
        Assert.Throws<InvalidOperationException>(() => new OllamaSettings { TimeoutSeconds = 0 }.EnsureValid());
    }

    [Fact]
    public void The_app_refuses_to_start_when_the_configured_dimensions_differ_from_the_database_column()
    {
        using var factory = new TestApp().WithWebHostBuilder(builder =>
            builder.UseSetting("Ollama:EmbeddingDimensions", "768"));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("1024", error.ToString());
    }
}
