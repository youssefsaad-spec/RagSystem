namespace RagSystem.Ingestion;

/// <summary>Everything the chunking of one file produced.</summary>
public sealed record IngestionResult
{
    public required string FileName { get; init; }

    public required string Version { get; init; }

    /// <summary>One of <see cref="SourceTypes"/>.</summary>
    public required string SourceType { get; init; }

    /// <summary>"guide" or "whats-new" for Markdown files, null for OpenAPI files.</summary>
    public string? DocType { get; init; }

    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    public required IReadOnlyList<SectionDraft> Sections { get; init; }

    public required IReadOnlyList<ChunkDraft> Chunks { get; init; }

    /// <summary>Names this file adds to its version's entity list.</summary>
    public required IReadOnlyList<string> EntityNames { get; init; }

    /// <summary>Counts for the report, for example headings or endpoint sections.</summary>
    public required IReadOnlyDictionary<string, int> Stats { get; init; }

    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

    /// <summary>True when there are no errors. Warnings do not make a file invalid.</summary>
    public bool IsValid => !Errors.Any();
}
