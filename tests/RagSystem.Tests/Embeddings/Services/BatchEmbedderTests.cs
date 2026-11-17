using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Embeddings.Services;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Embeddings.Services;

public class BatchEmbedderTests
{
    /// <summary>The waits the embedder asked for, instead of really waiting.</summary>
    private readonly List<TimeSpan> _waits = [];

    private BatchEmbedder EmbedderFor(StubHandler handler) =>
        new(OllamaTestGenerators.For(handler),
            NullLogger<BatchEmbedder>.Instance,
            (wait, _) =>
            {
                _waits.Add(wait);
                return Task.CompletedTask;
            });

    private static string[] InputOf(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        return body.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    /// <summary>
    /// Answers every request with one vector per input text. For "text 12" the vector starts 12, 1, 0, 0 ... so after the
    /// vector is scaled to length 1, first / second is still 12: a test can tell which vector belongs to which text.
    /// </summary>
    private static HttpResponseMessage Answer(HttpRequestMessage request) =>
        StubHandler.Reply(HttpStatusCode.OK, StubHandler.EmbeddingsJson(InputOf(request).Select(NumberedVector)));

    private static float[] NumberedVector(string text)
    {
        var vector = StubHandler.Vector(float.Parse(text.Split(' ')[1]));
        vector[1] = 1f;
        return vector;
    }

    private static List<string> Texts(int count) => Enumerable.Range(0, count).Select(i => $"text {i}").ToList();

    private static HttpResponseMessage Failure(HttpStatusCode status, int? retryAfterSeconds = null)
    {
        var response = StubHandler.Reply(status, "{\"error\":\"failed\"}");
        if (retryAfterSeconds is { } seconds)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    // ---- Result ----

    [Fact]
    public async Task No_texts_give_no_vectors_and_no_call()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(Answer(request)));

        var vectors = await EmbedderFor(handler).EmbedAsync([]);

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Every_text_gets_its_own_vector_in_the_original_order_across_batches()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(Answer(request)));

        var vectors = await EmbedderFor(handler).EmbedAsync(Texts(70));

        Assert.Equal(3, handler.Requests.Count); // 32 + 32 + 6
        Assert.Equal(70, vectors.Count);
        for (var i = 0; i < 70; i++)
            Assert.Equal(i, vectors[i][0] / vectors[i][1], precision: 3);
    }

    [Fact]
    public async Task Each_batch_holds_the_planned_consecutive_texts()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(Answer(request)));

        await EmbedderFor(handler).EmbedAsync(Texts(40));

        var sent = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("input").GetArrayLength()).ToArray();
        Assert.Equal([32, 8], sent);
    }

    [Fact]
    public async Task The_returned_vectors_have_length_1()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, StubHandler.EmbeddingsJson([StubHandler.Vector(5f), StubHandler.Vector(0.5f)]));

        var vectors = await EmbedderFor(handler).EmbedAsync(["a", "b"]);

        Assert.All(vectors, v => Assert.Equal(1.0, Math.Sqrt(v.Sum(x => (double)x * x)), precision: 5));
        Assert.All(vectors, v => Assert.Equal(1f, v[0], precision: 5));
    }

    // ---- Retry ----

    [Fact]
    public async Task Only_the_batch_that_failed_is_sent_again_and_finished_batches_are_kept()
    {
        var calls = 0;
        var handler = new StubHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(calls == 2 ? Failure(HttpStatusCode.ServiceUnavailable) : Answer(request)); // the second batch fails once
        });

        var vectors = await EmbedderFor(handler).EmbedAsync(Texts(70));

        Assert.Equal(70, vectors.Count);
        Assert.Equal(4, handler.Requests.Count); // batch 1, batch 2 (failed), batch 2 again, batch 3
        var firstTextOfEachRequest = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("input")[0].GetString()!).ToArray();
        Assert.Equal(new[] { "text 0", "text 32", "text 32", "text 64" }, firstTextOfEachRequest);
    }

    [Fact]
    public async Task Without_a_retry_after_it_waits_2_seconds_and_doubles_up_to_60()
    {
        var failures = 6;
        var handler = new StubHandler((request, _) =>
            Task.FromResult(failures-- > 0 ? Failure(HttpStatusCode.InternalServerError) : Answer(request)));

        var vectors = await EmbedderFor(handler).EmbedAsync(Texts(1));

        Assert.Single(vectors);
        Assert.Equal(new[] { 2, 4, 8, 16, 32, 60 }.Select(seconds => TimeSpan.FromSeconds(seconds)), _waits);
    }

    [Fact]
    public async Task The_servers_retry_after_is_waited_when_it_gives_one()
    {
        var calls = 0;
        var handler = new StubHandler((request, _) =>
            Task.FromResult(calls++ == 0 ? Failure(HttpStatusCode.TooManyRequests, retryAfterSeconds: 7) : Answer(request)));

        await EmbedderFor(handler).EmbedAsync(Texts(1));

        Assert.Equal([TimeSpan.FromSeconds(7)], _waits);
    }

    [Fact]
    public async Task A_refused_connection_is_retried()
    {
        var calls = 0;
        var handler = new StubHandler((request, _) =>
            calls++ == 0 ? throw new HttpRequestException("Connection refused") : Task.FromResult(Answer(request)));

        var vectors = await EmbedderFor(handler).EmbedAsync(Texts(1));

        Assert.Single(vectors);
        Assert.Equal([TimeSpan.FromSeconds(2)], _waits);
    }

    [Fact]
    public async Task It_gives_up_after_7_attempts_with_a_clear_error_that_keeps_the_cause()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Failure(HttpStatusCode.ServiceUnavailable)));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => EmbedderFor(handler).EmbedAsync(Texts(1)));

        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal(6, _waits.Count);
        Assert.Contains("Gave up on batch 1/1 after 7 attempts", error.Message);
        Assert.Contains("503", error.Message);
        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.IsType<EmbeddingException>(error.InnerException);
    }

    [Fact]
    public async Task A_fatal_error_fails_at_once_without_waiting()
    {
        var handler = StubHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"model not found\"}");

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => EmbedderFor(handler).EmbedAsync(Texts(1)));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Single(handler.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task A_vector_of_the_wrong_size_fails_at_once()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, StubHandler.EmbeddingsJson([StubHandler.Vector(1, length: 768)]));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => EmbedderFor(handler).EmbedAsync(Texts(1)));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_fatal_error_in_a_later_batch_stops_the_rest()
    {
        var calls = 0;
        var handler = new StubHandler((request, _) =>
            Task.FromResult(++calls == 2 ? Failure(HttpStatusCode.BadRequest) : Answer(request)));

        await Assert.ThrowsAsync<EmbeddingException>(() => EmbedderFor(handler).EmbedAsync(Texts(100)));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Cancelling_while_it_waits_stops_it_and_is_not_retried()
    {
        using var source = new CancellationTokenSource();
        var handler = new StubHandler((_, _) => Task.FromResult(Failure(HttpStatusCode.ServiceUnavailable)));
        var embedder = new BatchEmbedder(
            OllamaTestGenerators.For(handler),
            NullLogger<BatchEmbedder>.Instance,
            async (_, token) =>
            {
                await source.CancelAsync();
                token.ThrowIfCancellationRequested();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => embedder.EmbedAsync(Texts(1), source.Token));

        Assert.Single(handler.Requests);
    }
}
