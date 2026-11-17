using RagSystem.Ingestion.Models;

namespace RagSystem.Documents.Models;

/// <summary>It was a dry run, or the file is not valid. Nothing was stored.</summary>
public sealed record SubmitReport(IngestionResult Result, bool DryRun) : SubmitOutcome;
