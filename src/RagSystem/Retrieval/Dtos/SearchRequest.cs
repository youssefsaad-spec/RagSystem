namespace RagSystem.Retrieval.Dtos;

/// <summary>What a search sends. Checked by <c>SearchRequestValidator</c>.</summary>
/// <param name="Version">The version in the route, as the caller wrote it.</param>
/// <param name="Question">The <c>q</c> query parameter.</param>
public sealed record SearchRequest(string? Version, string? Question);
