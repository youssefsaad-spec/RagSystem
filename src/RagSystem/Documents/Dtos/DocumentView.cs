namespace RagSystem.Documents.Dtos;

/// <summary>A document as the list and detail answers show it.</summary>
public sealed record DocumentView(
    Guid Id,
    string Version,
    string FileName,
    string Title,
    string Status,
    int ChunkCount,
    string? Error,
    DateTimeOffset UploadedAt,
    DateTimeOffset? ReadyAt);
