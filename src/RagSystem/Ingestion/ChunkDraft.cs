using RagSystem.Text;

namespace RagSystem.Ingestion;

/// <summary>One retrievable piece of a document. Ids are given when the chunk is stored.</summary>
public sealed record ChunkDraft
{
    /// <summary>Position of the chunk in the file, starting at 0.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The <see cref="SectionDraft.Index"/> of the section this chunk comes from.</summary>
    public required int SectionIndex { get; init; }

    /// <summary>One of <see cref="ChunkTypes"/>.</summary>
    public required string ChunkType { get; init; }

    /// <summary>Document title followed by the heading path.</summary>
    public required IReadOnlyList<string> SectionPath { get; init; }

    public required string Title { get; init; }

    /// <summary>Names found in the text (endpoint, model and field names), used to boost search.</summary>
    public required IReadOnlyList<string> Entities { get; init; }

    /// <summary>Extra facts by type: route and method, flow, faqId, model.</summary>
    public required IReadOnlyDictionary<string, object?> Extra { get; init; }

    /// <summary>1-based part number when a section was split, otherwise 1.</summary>
    public required int PartIndex { get; init; }

    public required int PartCount { get; init; }

    /// <summary>The text between the brackets of the first line, for example "NDC Portal APIs V3 › Chapter 4 › FareCheck".</summary>
    public required string Breadcrumb { get; init; }

    /// <summary>Everything after the breadcrumb line.</summary>
    public required string Body { get; init; }

    /// <summary>What is embedded and shown to the model: the breadcrumb in brackets, a new line, then the body.</summary>
    public string Content => $"[{Breadcrumb}]\n{Body}";

    public int EstTokens => TokenEstimator.Estimate(Content);
}
