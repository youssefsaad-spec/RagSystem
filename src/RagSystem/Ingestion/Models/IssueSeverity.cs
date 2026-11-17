namespace RagSystem.Ingestion.Models;

/// <summary>An error rejects the file (422). A warning is only reported.</summary>
public enum IssueSeverity
{
    Warning,
    Error,
}
