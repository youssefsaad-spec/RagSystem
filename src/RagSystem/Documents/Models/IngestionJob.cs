using RagSystem.Ingestion.Models;

namespace RagSystem.Documents.Models;

/// <summary>A valid, chunked file waiting to be embedded and stored for its document.</summary>
public sealed record IngestionJob(Guid DocumentId, string Version, string FileName, IngestionResult Result);
