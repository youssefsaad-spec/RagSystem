using System.Text;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.OpenApi;

namespace RagSystem.Ingestion;

/// <summary>Turns an uploaded file into an <see cref="IngestionResult"/>, choosing the chunker by the file extension.</summary>
public sealed class DocumentProcessor(MarkdownChunker markdown, OpenApiChunker openApi)
{
    /// <param name="fileName">Only the name, never a path.</param>
    /// <param name="content">The file as uploaded, UTF-8 text.</param>
    /// <param name="version">For example "v3".</param>
    /// <exception cref="UnsupportedFileTypeException">The extension is not .md or .json.</exception>
    public IngestionResult Process(string fileName, byte[] content, string version)
    {
        var extension = Path.GetExtension(fileName);
        var isMarkdown = extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
        var isOpenApi = extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (!isMarkdown && !isOpenApi)
            throw new UnsupportedFileTypeException(fileName);

        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        return isMarkdown
            ? markdown.Chunk(fileName, text, version)
            : openApi.Chunk(fileName, text, version);
    }
}
