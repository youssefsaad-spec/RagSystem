using RagSystem.Ingestion.Models;

namespace RagSystem.Ingestion.Markdown.Models;

/// <summary>A file split into its header and its body.</summary>
/// <param name="FrontMatter">Null when the header is missing, broken or lacks a required field.</param>
/// <param name="Body">Everything after the header (the whole text when there is no header), with "\n" line ends.</param>
/// <param name="BodyStartLine">1-based line number in the original file where <paramref name="Body"/> starts.</param>
/// <param name="Issues">Every problem found. Errors reject the file.</param>
public sealed record FrontMatterResult(
    FrontMatter? FrontMatter,
    string Body,
    int BodyStartLine,
    IReadOnlyList<ValidationIssue> Issues);
