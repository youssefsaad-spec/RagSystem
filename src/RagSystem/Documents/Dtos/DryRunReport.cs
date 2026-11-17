using RagSystem.Ingestion.Models;

namespace RagSystem.Documents.Dtos;

/// <summary>What a dry run answers (and an invalid upload, without its chunks): the file was read and cut, nothing was stored.</summary>
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
