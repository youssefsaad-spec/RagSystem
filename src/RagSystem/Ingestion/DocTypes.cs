namespace RagSystem.Ingestion;

/// <summary>The kind of a Markdown document, written in its front matter as <c>docType</c>.</summary>
public static class DocTypes
{
    public const string Guide = "guide";
    public const string WhatsNew = "whats-new";

    public static readonly IReadOnlyList<string> All = [Guide, WhatsNew];
}
