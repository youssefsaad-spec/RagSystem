namespace RagSystem.Documents.Dtos;

/// <summary>What a call that only needs a version sends (list, get, chunks, delete). Checked by <c>VersionRequestValidator</c>.</summary>
/// <param name="Version">The <c>version</c> query parameter, as the caller wrote it.</param>
public sealed record VersionRequest(string? Version);
