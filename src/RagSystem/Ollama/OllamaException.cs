namespace RagSystem.Ollama;

/// <summary>How a failed Ollama call should be handled. Shared by the embedder (F13) and, later, the chat calls.</summary>
public enum OllamaFailureKind
{
    /// <summary>Retrying will not help: the model is not pulled (404), another 4xx, or an answer we cannot use.</summary>
    Fatal,

    /// <summary>Ollama said "too many requests" (429). Wait <see cref="OllamaException.RetryAfter"/> if it gave one.</summary>
    RateLimited,

    /// <summary>Worth another try: a server error (5xx), a refused connection (Ollama is starting) or a timeout.</summary>
    Transient,
}

public sealed class OllamaException(OllamaFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public OllamaFailureKind Kind { get; } = kind;

    /// <summary>The wait Ollama asked for with a Retry-After header. Null when it gave none.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
