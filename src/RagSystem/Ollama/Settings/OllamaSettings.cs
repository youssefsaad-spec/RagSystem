using RagSystem.Database;

namespace RagSystem.Ollama.Settings;

/// <summary>The "Ollama" section of appsettings.json. Ollama runs locally and needs no key.</summary>
public sealed record OllamaSettings
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; init; } = "http://localhost:11434";

    public string EmbeddingModel { get; init; } = "qwen3-embedding:0.6b";

    /// <summary>Must equal the size of the database column (1,024); the app refuses to start otherwise.</summary>
    public int EmbeddingDimensions { get; init; } = RagDbContext.EmbeddingDimensions;

    /// <summary>
    /// Run the embedding model on the CPU (<c>num_gpu: 0</c>). Useful on a small GPU, where the chat model fills the memory
    /// and Ollama would otherwise unload one model to load the other on every request.
    /// </summary>
    public bool EmbeddingOnCpu { get; init; }

    /// <summary>How long one call may take before it counts as a transient failure.</summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>Returns this object, or throws when a value cannot work.</summary>
    public OllamaSettings EnsureValid()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{SectionName}:BaseUrl must be an http or https address, but it is '{BaseUrl}'.");
        if (string.IsNullOrWhiteSpace(EmbeddingModel))
            throw new InvalidOperationException($"{SectionName}:EmbeddingModel must not be empty.");
        if (EmbeddingDimensions != RagDbContext.EmbeddingDimensions)
            throw new InvalidOperationException(
                $"{SectionName}:EmbeddingDimensions is {EmbeddingDimensions}, but the database column holds {RagDbContext.EmbeddingDimensions}. " +
                "Another size needs a migration and a full re-embed.");
        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException($"{SectionName}:TimeoutSeconds must be above 0, but it is {TimeoutSeconds}.");
        return this;
    }
}
