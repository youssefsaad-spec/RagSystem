namespace RagSystem.Ingestion.Models;

/// <summary>One problem found in a file, for example Code "frontmatter.missing". Line is 1-based when known.</summary>
public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, int? Line = null);
