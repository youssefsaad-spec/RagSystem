namespace RagSystem.Embeddings.Exceptions;

public sealed class EmbeddingException(EmbeddingFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public EmbeddingFailureKind Kind { get; } = kind;

    /// <summary>The wait the service asked for with a Retry-After header. Null when it gave none.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
