namespace RagSystem.Retrieval.Models;

/// <summary>What post-processing decided: the sources in the order the model reads them, and every candidate that was left out.</summary>
/// <param name="Dropped">In rank order (best match first).</param>
public sealed record RetrievalResult(IReadOnlyList<RetrievedSource> Sources, IReadOnlyList<DroppedCandidate> Dropped)
{
    /// <summary>No relevant chunk is left: the question is "not covered" and the model is not called (F25).</summary>
    public bool IsEmpty => Sources.Count == 0;
}
