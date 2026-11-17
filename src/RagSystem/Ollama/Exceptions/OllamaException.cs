namespace RagSystem.Ollama.Exceptions;

public sealed class OllamaException(OllamaFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public OllamaFailureKind Kind { get; } = kind;

    /// <summary>The wait Ollama asked for with a Retry-After header. Null when it gave none.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
