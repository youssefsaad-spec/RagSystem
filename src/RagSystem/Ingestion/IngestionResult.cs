namespace RagSystem.Ingestion;

/// <summary>Everything the chunking of one file produced.</summary>
public sealed record IngestionResult
{
    public required string FileName { get; init; }

    public required string Version { get; init; }

    /// <summary>The name shown in citations: the front matter title, or the file name for an OpenAPI file.</summary>
    public required string Title { get; init; }

    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    public required IReadOnlyList<ChunkDraft> Chunks { get; init; }

    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

    /// <summary>True when there are no errors. Warnings do not make a file invalid.</summary>
    public bool IsValid => !Errors.Any();
}
