using Pgvector;

namespace RagSystem.Database;

/// <summary>One searchable piece of a document: the text exactly as it was cut, and its vector.</summary>
public sealed class Chunk
{
    public Guid Id { get; set; }

    /// <summary>The document it belongs to. Deleting the document deletes its chunks.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Copy of the document's version, so a search filters on one table.</summary>
    public string Version { get; set; } = "";

    /// <summary>Position in the document, starting at 0.</summary>
    public int Ordinal { get; set; }

    public string Content { get; set; } = "";

    public int EstTokens { get; set; }

    public Vector Embedding { get; set; } = null!;

    /// <summary>The model that made the embedding, for example "qwen3-embedding:0.6b".</summary>
    public string EmbeddingModel { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
