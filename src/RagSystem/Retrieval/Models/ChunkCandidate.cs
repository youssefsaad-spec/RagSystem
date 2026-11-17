namespace RagSystem.Retrieval.Models;

/// <summary>One chunk found by the vector search, with what the later steps need.</summary>
/// <param name="Ordinal">Position in the document, starting at 0 (as stored).</param>
/// <param name="Embedding">Kept for the near-duplicate check (F19).</param>
/// <param name="Score">Cosine similarity to the question: 1 minus the cosine distance.</param>
public sealed record ChunkCandidate(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Ordinal,
    string Content,
    int EstTokens,
    float[] Embedding,
    double Score);
