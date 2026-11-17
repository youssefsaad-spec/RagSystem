using RagSystem.Ingestion;

namespace RagSystem.Documents;

/// <summary>What a dry run answers: the file was read and cut, nothing was stored.</summary>
public sealed record DryRunReport(
    string FileName,
    string Version,
    string Title,
    bool Valid,
    IReadOnlyList<IssueReport> Errors,
    IReadOnlyList<IssueReport> Warnings,
    IReadOnlyList<ChunkReport> Chunks)
{
    public static DryRunReport From(IngestionResult result) => new(
        result.FileName,
        result.Version,
        result.Title,
        result.IsValid,
        result.Errors.Select(IssueReport.From).ToList(),
        result.Warnings.Select(IssueReport.From).ToList(),
        result.Chunks.Select(ChunkReport.From).ToList());
}

public sealed record IssueReport(string Code, string Message, int? Line)
{
    public static IssueReport From(ValidationIssue issue) => new(issue.Code, issue.Message, issue.Line);
}

public sealed record ChunkReport(int Ordinal, string Text, int EstTokens)
{
    public static ChunkReport From(ChunkDraft chunk) => new(chunk.Ordinal, chunk.Text, chunk.EstTokens);
}
