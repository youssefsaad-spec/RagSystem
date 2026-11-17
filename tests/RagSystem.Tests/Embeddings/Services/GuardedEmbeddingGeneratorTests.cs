using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Embeddings.Services;

namespace RagSystem.Tests.Embeddings.Services;

/// <summary>What the app does with any provider behind the Microsoft.Extensions.AI interface, shown with a fake one that fails like Gemini.</summary>
public class GuardedEmbeddingGeneratorTests
{
    private const int Size = 1024;

    private sealed class FakeGenerator(Func<IEnumerable<string>, CancellationToken, GeneratedEmbeddings<Embedding<float>>> answer)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(answer(values, cancellationToken));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static GeneratedEmbeddings<Embedding<float>> VectorsFor(IEnumerable<string> texts, int size = Size) =>
        new(texts.Select(_ => new Embedding<float>(Enumerable.Range(0, size).Select(i => i == 0 ? 1f : 0f).ToArray())));

    private static FakeGenerator Working() => new((texts, _) => VectorsFor(texts));

    private static FakeGenerator Throwing(Exception error) => new((_, _) => throw error);

    private static GuardedEmbeddingGenerator Guard(IEmbeddingGenerator<string, Embedding<float>> inner) =>
        new("Gemini", Size, () => inner);

    private static async Task<EmbeddingException> FailureOf(Exception error) =>
        await Assert.ThrowsAsync<EmbeddingException>(() => Guard(Throwing(error)).GenerateAsync(["text"]));

    [Fact]
    public async Task Good_vectors_pass_through_unchanged()
    {
        var result = await Guard(Working()).GenerateAsync(["a", "b"]);

        Assert.Equal(2, result.Count);
        Assert.Equal(Size, result[0].Vector.Length);
    }

    [Fact]
    public async Task No_texts_give_no_vectors_and_no_call()
    {
        var inner = Working();

        var result = await Guard(inner).GenerateAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task Gemini_429_is_rate_limited()
    {
        var error = await FailureOf(new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!));

        Assert.Equal(EmbeddingFailureKind.RateLimited, error.Kind);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(408)]
    public async Task Gemini_5xx_and_408_are_transient(int status)
    {
        var error = await FailureOf(new ServerError("busy", status, "UNAVAILABLE", null!));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Theory]
    [InlineData(400)] // bad request
    [InlineData(401)]
    [InlineData(403)] // wrong or blocked API key
    [InlineData(404)] // model not found
    public async Task Gemini_other_4xx_are_fatal(int status)
    {
        var error = await FailureOf(new ClientError("no", status, "INVALID_ARGUMENT", null!));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains(status.ToString(), error.Message);
    }

    [Fact]
    public async Task A_connection_failure_is_transient()
    {
        var error = await FailureOf(new HttpRequestException("Connection refused"));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Fact]
    public async Task A_timeout_that_the_caller_did_not_ask_for_is_transient()
    {
        var error = await FailureOf(new TaskCanceledException("timed out"));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_turned_into_an_error()
    {
        using var source = new CancellationTokenSource();
        var inner = new FakeGenerator((_, token) =>
        {
            source.Cancel();
            token.ThrowIfCancellationRequested();
            return [];
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guard(inner).GenerateAsync(["a"], cancellationToken: source.Token));
    }

    [Fact]
    public async Task A_vector_of_the_wrong_size_is_fatal()
    {
        var inner = new FakeGenerator((texts, _) => VectorsFor(texts, size: 768));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => Guard(inner).GenerateAsync(["a"]));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("768", error.Message);
    }

    [Fact]
    public async Task A_wrong_number_of_vectors_is_fatal()
    {
        var inner = new FakeGenerator((_, _) => VectorsFor(["only one"]));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => Guard(inner).GenerateAsync(["a", "b"]));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
    }

    [Fact]
    public async Task A_generator_that_cannot_be_built_is_a_fatal_error_with_its_message_and_is_tried_again_next_time()
    {
        var builds = 0;
        var guard = new GuardedEmbeddingGenerator("Gemini", Size, () =>
        {
            builds++;
            throw new InvalidOperationException("Gemini:ApiKey is not set.");
        });

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => guard.GenerateAsync(["a"]));
        await Assert.ThrowsAsync<EmbeddingException>(() => guard.GenerateAsync(["a"]));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("Gemini:ApiKey", error.Message);
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task The_batch_embedder_retries_a_Gemini_429_and_then_succeeds()
    {
        var calls = 0;
        var inner = new FakeGenerator((texts, _) =>
            ++calls == 1 ? throw new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!) : VectorsFor(texts));
        var waits = new List<TimeSpan>();
        var embedder = new BatchEmbedder(Guard(inner), NullLogger<BatchEmbedder>.Instance, (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        });

        var vectors = await embedder.EmbedAsync(["a", "b"]);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(2, calls);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task The_batch_embedder_does_not_retry_a_Gemini_bad_key()
    {
        var inner = Throwing(new ClientError("API key not valid", 403, "PERMISSION_DENIED", null!));

        var error = await Assert.ThrowsAsync<EmbeddingException>(
            () => new BatchEmbedder(Guard(inner), NullLogger<BatchEmbedder>.Instance, (_, _) => Task.CompletedTask).EmbedAsync(["a"]));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task The_query_embedder_scales_the_vector_to_length_1_through_the_interface()
    {
        var inner = new FakeGenerator((texts, _) =>
            new(texts.Select(_ => new Embedding<float>(Enumerable.Range(0, Size).Select(i => i == 0 ? 5f : 0f).ToArray()))));

        var vector = await new QueryEmbedder(Guard(inner)).EmbedAsync("hello");

        Assert.Equal(1f, vector[0]);
    }
}
