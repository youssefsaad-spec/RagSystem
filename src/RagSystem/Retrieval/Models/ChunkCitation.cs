namespace RagSystem.Retrieval.Models;

/// <summary>What a citation is built from: the document's title and the chunk's position in it (starting at 0, as stored).</summary>
public sealed record ChunkCitation(Guid ChunkId, string DocumentTitle, int Ordinal);
