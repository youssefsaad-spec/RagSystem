namespace RagSystem.Embeddings.Settings;

/// <summary>Which service makes the embeddings.</summary>
public enum EmbeddingProvider
{
    /// <summary>The Gemini API (key in <c>Gemini:ApiKey</c>). Nothing runs locally.</summary>
    Gemini,

    /// <summary>A local Ollama server (the "Ollama" section).</summary>
    Ollama,
}

/// <summary>The "Embeddings" section of appsettings.json.</summary>
public sealed record EmbeddingSettings
{
    public const string SectionName = "Embeddings";

    public EmbeddingProvider Provider { get; init; } = EmbeddingProvider.Gemini;
}

/// <summary>
/// Which embedding model is in use, as stored with every chunk (<c>chunks.embedding_model</c>). Vectors from different models
/// cannot be compared, so after switching provider or model every document must be uploaded again.
/// </summary>
public sealed record EmbeddingModelInfo(EmbeddingProvider Provider, string Name, int Dimensions);
