namespace RagSystem.Ingestion.Markdown.Models;

/// <summary>The header block at the top of a documentation file.</summary>
/// <param name="Version">Lower case, for example "v3".</param>
/// <param name="Title">Becomes the first part of every breadcrumb.</param>
/// <param name="Source">Where the text came from. Optional, for information only.</param>
public sealed record FrontMatter(string Version, string Title, string DocType, string Updated, string? Source);
