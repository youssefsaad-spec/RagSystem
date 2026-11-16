namespace RagSystem.Ingestion;

/// <summary>
/// How text is cut into chunks. The start values come from trying Semantic Kernel's chunker on the real files;
/// they are meant to be tuned later (F46).
/// </summary>
public sealed record ChunkingSettings
{
    /// <summary>Largest chunk, in estimated tokens.</summary>
    public int MaxTokensPerChunk { get; init; } = 500;

    /// <summary>
    /// Largest "line" the chunker works with before it packs lines into chunks. A line longer than this is cut
    /// inside. A small value cuts table rows more often.
    /// </summary>
    public int MaxTokensPerLine { get; init; } = 500;

    /// <summary>Tokens repeated at the start of the next chunk. 0 = no overlap.</summary>
    public int OverlapTokens { get; init; } = 0;
}
