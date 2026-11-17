namespace RagSystem.Retrieval.Models;

/// <summary>
/// A chunk chosen as documentation for the answer. The model cites it by <paramref name="Label"/> (<c>S1</c>, <c>S2</c>, ...),
/// which is only valid for this one question.
/// </summary>
/// <param name="Ordinal">Position in the document, starting at 0 (as stored).</param>
public sealed record RetrievedSource(
    string Label,
    string DocumentTitle,
    int Ordinal,
    string Content,
    int EstTokens,
    Guid ChunkId,
    double Score);
