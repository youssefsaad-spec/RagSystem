using RagSystem.Ingestion.Models;

namespace RagSystem.Documents.Dtos;

/// <summary>One chunk of an uploaded file, as the report shows it.</summary>
public sealed record ChunkReport(int Ordinal, string Text, int EstTokens)
{
    public static ChunkReport From(ChunkDraft chunk) => new(chunk.Ordinal, chunk.Text, chunk.EstTokens);
}
