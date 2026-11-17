using RagSystem.Ingestion.Models;

namespace RagSystem.Documents.Dtos;

/// <summary>One problem found in an uploaded file, as the report shows it.</summary>
public sealed record IssueReport(string Code, string Message, int? Line)
{
    public static IssueReport From(ValidationIssue issue) => new(issue.Code, issue.Message, issue.Line);
}
