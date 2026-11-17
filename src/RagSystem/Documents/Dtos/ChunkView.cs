namespace RagSystem.Documents.Dtos;

/// <summary>A stored chunk as the chunk browser shows it. The vector is left out: it is 1,024 numbers nobody reads.</summary>
public sealed record ChunkView(Guid Id, int Ordinal, string Content, int EstTokens, string EmbeddingModel);
