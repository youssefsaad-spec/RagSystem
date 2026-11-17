namespace RagSystem.Ingestion.Settings;

/// <summary>
/// How text is cut into chunks. The values live in the "Chunking" section of appsettings.json; the defaults here
/// are the same values and are used when a value is missing. They are meant to be tuned later (F28).
/// </summary>
public sealed record ChunkingSettings
{
    public const string SectionName = "Chunking";

    /// <summary>Largest chunk, in estimated tokens.</summary>
    public int MaxTokensPerChunk { get; init; } = 500;

    /// <summary>
    /// Largest "line" the chunker works with before it packs lines into chunks. A line longer than this is cut
    /// inside. A small value cuts table rows more often.
    /// </summary>
    public int MaxTokensPerLine { get; init; } = 500;

    /// <summary>
    /// Tokens repeated at the start of the next chunk, so a sentence cut at a boundary can be found from both sides.
    /// 100 is 20% of a chunk. 0 = no overlap.
    /// </summary>
    public int OverlapTokens { get; init; } = 100;

    /// <summary>Returns this object, or throws when a value cannot work.</summary>
    public ChunkingSettings EnsureValid()
    {
        if (MaxTokensPerChunk <= 0)
            throw new InvalidOperationException($"{SectionName}:MaxTokensPerChunk must be above 0, but it is {MaxTokensPerChunk}.");
        if (MaxTokensPerLine <= 0)
            throw new InvalidOperationException($"{SectionName}:MaxTokensPerLine must be above 0, but it is {MaxTokensPerLine}.");
        if (OverlapTokens < 0 || OverlapTokens >= MaxTokensPerChunk)
            throw new InvalidOperationException(
                $"{SectionName}:OverlapTokens must be from 0 up to (but not including) MaxTokensPerChunk ({MaxTokensPerChunk}), but it is {OverlapTokens}.");
        return this;
    }
}
