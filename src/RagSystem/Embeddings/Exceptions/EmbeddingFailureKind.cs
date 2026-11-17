namespace RagSystem.Embeddings.Exceptions;

/// <summary>How a failed embedding call (any provider: Gemini, Ollama) should be handled. Shared by the embedder (F13) and, later, the chat calls.</summary>
public enum EmbeddingFailureKind
{
    /// <summary>Retrying will not help: the model is not found (404), a bad key or request (other 4xx), or an answer we cannot use.</summary>
    Fatal,

    /// <summary>The service said "too many requests" (429). Wait <see cref="EmbeddingException.RetryAfter"/> if it gave one.</summary>
    RateLimited,

    /// <summary>Worth another try: a server error (5xx), a refused connection (the service is starting) or a timeout.</summary>
    Transient,
}
