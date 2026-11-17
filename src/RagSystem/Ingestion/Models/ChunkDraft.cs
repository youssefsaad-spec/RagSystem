using RagSystem.Text;

namespace RagSystem.Ingestion.Models;

/// <summary>One piece of a document, exactly as the chunker cut it. It is stored and embedded as it is.</summary>
/// <param name="Ordinal">Position of the chunk in the file, starting at 0.</param>
public sealed record ChunkDraft(int Ordinal, string Text)
{
    public int EstTokens => TokenEstimator.Estimate(Text);
}
