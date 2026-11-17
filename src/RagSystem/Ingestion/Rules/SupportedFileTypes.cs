namespace RagSystem.Ingestion.Rules;

/// <summary>The file types we read: Markdown documentation and OpenAPI JSON.</summary>
public static class SupportedFileTypes
{
    public const string Markdown = ".md";

    public const string OpenApi = ".json";

    /// <summary>True for .md and .json, in any case.</summary>
    public static bool IsSupported(string fileName) => IsMarkdown(fileName) || IsOpenApi(fileName);

    public static bool IsMarkdown(string fileName) => Path.GetExtension(fileName).Equals(Markdown, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenApi(string fileName) => Path.GetExtension(fileName).Equals(OpenApi, StringComparison.OrdinalIgnoreCase);
}
