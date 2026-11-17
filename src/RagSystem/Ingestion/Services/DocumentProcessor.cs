using System.Text;
using RagSystem.Ingestion.Exceptions;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.Models;
using RagSystem.Ingestion.OpenApi;
using RagSystem.Ingestion.Rules;

namespace RagSystem.Ingestion.Services;

/// <summary>Turns an uploaded file into an <see cref="IngestionResult"/>, choosing the chunker by the file extension.</summary>
public sealed class DocumentProcessor(MarkdownChunker markdown, OpenApiChunker openApi)
{
    /// <param name="fileName">Only the name, never a path.</param>
    /// <param name="content">The file as uploaded, UTF-8 text.</param>
    /// <param name="version">For example "v3".</param>
    /// <exception cref="UnsupportedFileTypeException">The extension is not .md or .json (the upload endpoint refuses these earlier, in its validator).</exception>
    public IngestionResult Process(string fileName, byte[] content, string version)
    {
        if (!SupportedFileTypes.IsSupported(fileName))
            throw new UnsupportedFileTypeException(fileName);

        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var result = SupportedFileTypes.IsMarkdown(fileName)
            ? markdown.Chunk(fileName, text, version)
            : openApi.Chunk(fileName, text, version);

        // A file that gives no chunks would replace a document's content with nothing: refuse it.
        if (result.IsValid && result.Chunks.Count == 0)
            result = result with
            {
                Issues = [.. result.Issues, new ValidationIssue(IssueSeverity.Error, "document.empty", "The file has no text to store.")],
            };
        return result;
    }
}
