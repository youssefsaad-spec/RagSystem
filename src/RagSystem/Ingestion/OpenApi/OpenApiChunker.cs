using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Microsoft.SemanticKernel.Text;
using RagSystem.Ingestion.Models;
using RagSystem.Ingestion.Settings;
using RagSystem.Text;

namespace RagSystem.Ingestion.OpenApi;

/// <summary>
/// Cuts an OpenAPI (swagger) JSON file into chunks. Each operation and each component schema is written back as its
/// own JSON text; a text that is too long is cut by Semantic Kernel's <see cref="TextChunker"/>. Every chunk starts
/// with a header line that says what it belongs to (<c>POST /api/V3/FareCheck</c> or <c>Schema Passenger</c>).
/// </summary>
public sealed partial class OpenApiChunker(ChunkingSettings settings)
{
    private readonly ChunkingSettings _settings = settings.EnsureValid();

    public OpenApiChunker() : this(new ChunkingSettings())
    {
    }

    /// <summary>A path must look like /api/V3/Something. The number is the version of the upload.</summary>
    [GeneratedRegex(@"^/api/V(\d+)/", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedPath();

    /// <param name="version">The version the file is uploaded for, for example "v3".</param>
    public IngestionResult Chunk(string fileName, string json, string version)
    {
        var read = Read(json);
        if (read.Document is null)
            return Result(fileName, version, fileName, read.Issues, []);

        var document = read.Document;
        var issues = read.Issues;
        var title = string.IsNullOrWhiteSpace(document.Info?.Title) ? fileName : document.Info.Title;

        var paths = document.Paths ?? [];
        if (paths.Count == 0)
            issues.Add(new(IssueSeverity.Error, "openapi.empty", "The OpenAPI file has no paths."));
        CheckVersion(paths.Keys, version, issues);

        // Operations first, in file order, then schemas.
        var sources = new List<(string Header, string Text)>();

        foreach (var (path, item) in paths)
            foreach (var (method, operation) in item.Operations ?? [])
                sources.Add(($"{method.Method.ToUpperInvariant()} {path}", OpenApiJson.Write(operation)));

        if (document.Components?.Schemas is { } schemas)
            foreach (var (name, schema) in schemas)
                sources.Add(($"Schema {name}", OpenApiJson.Write(schema)));

        var pieces = sources.SelectMany(source => Cut(source.Header, source.Text));
        var chunks = pieces.Select((piece, index) => new ChunkDraft(index, piece)).ToList();
        return Result(fileName, version, title, issues, chunks);
    }

    private static (OpenApiDocument? Document, List<ValidationIssue> Issues) Read(string json)
    {
        var issues = new List<ValidationIssue>();
        try
        {
            var read = OpenApiDocument.Parse(json, "json");
            var errors = read.Diagnostic?.Errors ?? [];
            if (read.Document is null)
            {
                var reason = errors.Count > 0 ? errors[0].Message : "no document was found";
                issues.Add(new(IssueSeverity.Error, "openapi.unreadable", $"The file is not a readable OpenAPI document: {reason}"));
                return (null, issues);
            }
            issues.AddRange(errors.Select(e => new ValidationIssue(IssueSeverity.Error, "openapi.invalid", $"{e.Message} ({e.Pointer})")));
            return (read.Document, issues);
        }
        catch (Exception ex)
        {
            issues.Add(new(IssueSeverity.Error, "openapi.unreadable", $"The file is not a readable OpenAPI document: {ex.Message}"));
            return (null, issues);
        }
    }

    /// <summary>Every path must start with /api/V{n}/ for the version of the upload, else one error names the first wrong path.</summary>
    private static void CheckVersion(IEnumerable<string> paths, string version, List<ValidationIssue> issues)
    {
        var wrong = paths.Where(path =>
        {
            var match = VersionedPath().Match(path);
            return !match.Success || !string.Equals("v" + match.Groups[1].Value, version, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (wrong.Count == 0) return;

        issues.Add(new(IssueSeverity.Error, "version.mismatch",
            $"{wrong.Count} path(s) do not start with /api/{version.ToUpperInvariant()}/ for the upload version '{version}'. First one: {wrong[0]}"));
    }

    /// <summary>One chunk when header and text fit the limit; otherwise the text is cut and every piece gets the header.</summary>
    private IEnumerable<string> Cut(string header, string text)
    {
        var chunkHeader = header + "\n";
        if (TokenEstimator.Estimate(chunkHeader + text) <= _settings.MaxTokensPerChunk)
            return [chunkHeader + text];

        var lines = TextChunker.SplitPlainTextLines(text, _settings.MaxTokensPerLine, TokenEstimator.Estimate);
        return TextChunker.SplitPlainTextParagraphs(
                lines, _settings.MaxTokensPerChunk, _settings.OverlapTokens, chunkHeader, TokenEstimator.Estimate)
            .Where(piece => !string.IsNullOrWhiteSpace(piece));
    }

    private static IngestionResult Result(
        string fileName, string version, string title, IReadOnlyList<ValidationIssue> issues, IReadOnlyList<ChunkDraft> chunks) =>
        new() { FileName = fileName, Version = version, Title = title, Issues = issues, Chunks = chunks };
}
