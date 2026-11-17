namespace RagSystem.Retrieval.Rules;

/// <summary>How a chunk is named for a reader: the document title and the chunk's part number, for example "NDC Portal APIs V3, part 12".</summary>
public static class CitationText
{
    /// <param name="ordinal">The stored position, starting at 0. The first chunk is shown as "part 1".</param>
    public static string Of(string documentTitle, int ordinal) => $"{documentTitle}, part {ordinal + 1}";
}
