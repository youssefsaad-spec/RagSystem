namespace RagSystem.Ingestion;

/// <summary>
/// A whole heading section (or one OpenAPI operation). Kept next to its chunks so that retrieval can later
/// replace several small chunks of one section by the section itself.
/// </summary>
/// <param name="Index">Position in the file. Chunks point to their section by this number.</param>
/// <param name="Path">Heading path, for example ["Chapter 4. Endpoints", "Order", "FareCheck"].</param>
public sealed record SectionDraft(int Index, IReadOnlyList<string> Path, string Content, int EstTokens);
