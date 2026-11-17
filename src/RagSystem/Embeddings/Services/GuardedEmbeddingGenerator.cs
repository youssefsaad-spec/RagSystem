using Google.GenAI;
using Microsoft.Extensions.AI;
using RagSystem.Embeddings.Exceptions;

namespace RagSystem.Embeddings.Services;

/// <summary>
/// Wraps any Microsoft.Extensions.AI embedding generator (Gemini, Ollama, ...) so the rest of the app sees one behaviour:
/// every failure is an <see cref="EmbeddingException"/> with a <see cref="EmbeddingFailureKind"/> (fatal, rate-limited or
/// transient), and an answer with the wrong number of vectors or the wrong vector size is a fatal error. The inner generator is
/// created on first use, so the app starts even when a provider is not configured (for example no API key yet).
/// </summary>
/// <param name="provider">The provider's name, for error messages.</param>
/// <param name="dimensions">The vector size the database column holds.</param>
/// <param name="create">Builds the real generator. May throw when the provider is not configured.</param>
public sealed class GuardedEmbeddingGenerator(
    string provider,
    int dimensions,
    Func<IEmbeddingGenerator<string, Embedding<float>>> create) : IEmbeddingGenerator<string, Embedding<float>>
{
    // PublicationOnly: a failed creation is not remembered, the next call tries again.
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _inner = new(create, LazyThreadSafetyMode.PublicationOnly);

    /// <exception cref="EmbeddingException">The call failed or the answer is unusable.</exception>
    /// <exception cref="OperationCanceledException">Only when the caller cancelled. A timeout is a transient <see cref="EmbeddingException"/>.</exception>
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var texts = values as IReadOnlyCollection<string> ?? values.ToList();
        if (texts.Count == 0)
            return [];

        GeneratedEmbeddings<Embedding<float>> embeddings;
        try
        {
            embeddings = await _inner.Value.GenerateAsync(texts, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            throw Classify(provider, error);
        }

        if (embeddings.Count != texts.Count)
            throw new EmbeddingException(EmbeddingFailureKind.Fatal, $"{provider} returned {embeddings.Count} embeddings for {texts.Count} texts.");

        var wrong = embeddings.FirstOrDefault(e => e.Vector.Length != dimensions);
        if (wrong is not null)
            throw new EmbeddingException(EmbeddingFailureKind.Fatal, $"Expected {dimensions} dimensions but {provider} returned {wrong.Vector.Length}.");

        return embeddings;
    }

    /// <summary>The failure kind for an exception thrown by a provider SDK or HTTP client.</summary>
    public static EmbeddingException Classify(string provider, Exception error) => error switch
    {
        EmbeddingException already => already,

        // Gemini: the SDK reports the HTTP status. It does not expose Retry-After, so the caller's own wait is used.
        ApiException { StatusCode: 429 } api => new(EmbeddingFailureKind.RateLimited, $"{provider} answered 429: {api.Message}", inner: api),
        ApiException { StatusCode: 408 or >= 500 } api => new(EmbeddingFailureKind.Transient, $"{provider} answered {api.StatusCode}: {api.Message}", inner: api),
        ApiException api => new(EmbeddingFailureKind.Fatal, $"{provider} answered {api.StatusCode}: {api.Message}", inner: api), // bad key, bad request, model not found

        // Connection refused or reset, DNS failure.
        HttpRequestException http => new(EmbeddingFailureKind.Transient, $"{provider} could not be reached: {http.Message}", inner: http),

        // The call's own timeout (the caller did not cancel).
        OperationCanceledException timeout => new(EmbeddingFailureKind.Transient, $"{provider} timed out.", inner: timeout),

        // A missing API key or another setting problem: waiting will not fix it.
        InvalidOperationException setup => new(EmbeddingFailureKind.Fatal, setup.Message, inner: setup),

        _ => new(EmbeddingFailureKind.Fatal, $"{provider} embedding failed: {error.Message}", inner: error),
    };

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        if (_inner.IsValueCreated)
            _inner.Value.Dispose();
    }
}
