namespace RagSystem.Ingestion;

/// <summary>Where a document came from. These exact strings are stored in the database.</summary>
public static class SourceTypes
{
    public const string Markdown = "md";
    public const string OpenApi = "openapi";
}
