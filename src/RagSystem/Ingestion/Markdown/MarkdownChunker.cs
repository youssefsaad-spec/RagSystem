using Microsoft.SemanticKernel.Text;
using RagSystem.Text;

namespace RagSystem.Ingestion.Markdown;

/// <summary>
/// Cuts a Markdown documentation file into chunks. The header is read by <see cref="FrontMatterReader"/>; the body is
/// cut by Semantic Kernel's <see cref="TextChunker"/>, and the chunks are kept exactly as it returns them.
/// </summary>
public sealed class MarkdownChunker(ChunkingSettings settings)
{
    private readonly ChunkingSettings _settings = settings.EnsureValid();

    public MarkdownChunker() : this(new ChunkingSettings())
    {
    }

    /// <param name="version">The version the file is uploaded for, for example "v3".</param>
    /// <returns>
    /// The title, the issues and the chunks. A file without a usable header gets no chunks. A file whose header only
    /// has a wrong docType or version still gets its chunks, so a dry run can show them next to the errors.
    /// </returns>
    public IngestionResult Chunk(string fileName, string text, string version)
    {
        var file = FrontMatterReader.Read(text, version);
        if (file.FrontMatter is null)
            return Result(fileName, version, fileName, file.Issues, []);

        var lines = TextChunker.SplitMarkDownLines(file.Body, _settings.MaxTokensPerLine, TokenEstimator.Estimate);
        var pieces = TextChunker.SplitMarkdownParagraphs(
            lines, _settings.MaxTokensPerChunk, _settings.OverlapTokens, chunkHeader: null, TokenEstimator.Estimate);

        var chunks = pieces
            .Where(piece => !string.IsNullOrWhiteSpace(piece))
            .Select((piece, index) => new ChunkDraft(index, piece))
            .ToList();
        return Result(fileName, file.FrontMatter.Version, file.FrontMatter.Title, file.Issues, chunks);
    }

    private static IngestionResult Result(
        string fileName, string version, string title, IReadOnlyList<ValidationIssue> issues, IReadOnlyList<ChunkDraft> chunks) =>
        new() { FileName = fileName, Version = version, Title = title, Issues = issues, Chunks = chunks };
}
