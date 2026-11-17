using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.Models;
using RagSystem.Ingestion.Settings;

namespace RagSystem.Tests.Ingestion.Markdown;

public class MarkdownChunkerTests
{
    private const string Header = "---\nversion: v3\ntitle: \"NDC Portal APIs V3\"\ndocType: guide\nupdated: 2026-10-23\n---\n";

    /// <summary>A small limit with no overlap, so a short test file needs several chunks.</summary>
    private static readonly ChunkingSettings Small = new() { MaxTokensPerChunk = 40, MaxTokensPerLine = 40, OverlapTokens = 0 };

    /// <summary>A body with a heading, many paragraphs and a table, long enough to need several chunks at a small limit.</summary>
    private static string Body()
    {
        var text = new System.Text.StringBuilder("\n# NDC Portal APIs V3\n\n## Chapter 4. Endpoints\n\n");
        for (var i = 1; i <= 12; i++)
            text.Append($"Paragraph {i} explains how the FareCheck endpoint revalidates the price of offer {i}. It returns a new offerId.\n\n");
        text.Append("| Parameter | Description |\n|---|---|\n");
        for (var i = 1; i <= 6; i++)
            text.Append($"| Field{i} | Description of field number {i}, which is optional. |\n");
        return text.ToString();
    }

    private static string WithoutWhitespace(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string AllText(IngestionResult result) => string.Concat(result.Chunks.Select(c => c.Text));

    /// <summary>
    /// Checks that the chunks, in order, are slices of the document that together cover it from the first character
    /// to the last with no gap. Overlap (a chunk starting before the previous one ends) is allowed.
    /// </summary>
    private static void AssertCovers(string body, IReadOnlyList<ChunkDraft> chunks)
    {
        var document = WithoutWhitespace(body);
        var previousStart = 0;
        var coveredUpTo = 0;
        foreach (var chunk in chunks)
        {
            var text = WithoutWhitespace(chunk.Text);
            var at = document.IndexOf(text, previousStart, StringComparison.Ordinal);
            Assert.True(at >= 0, $"chunk {chunk.Ordinal} is not a piece of the document");
            Assert.True(at <= coveredUpTo, $"text is missing between chunk {chunk.Ordinal - 1} and chunk {chunk.Ordinal}");
            previousStart = at;
            coveredUpTo = Math.Max(coveredUpTo, at + text.Length);
        }
        Assert.Equal(document.Length, coveredUpTo);
    }

    [Fact]
    public void A_valid_file_gives_its_title_version_file_name_and_no_issues()
    {
        var result = new MarkdownChunker().Chunk("ndc.md", Header + Body(), "v3");

        Assert.Empty(result.Issues);
        Assert.True(result.IsValid);
        Assert.Equal(("ndc.md", "v3", "NDC Portal APIs V3"), (result.FileName, result.Version, result.Title));
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public void The_header_is_not_part_of_any_chunk()
    {
        var result = new MarkdownChunker().Chunk("ndc.md", Header + Body(), "v3");

        var all = AllText(result);
        Assert.DoesNotContain("docType", all);
        Assert.DoesNotContain("updated:", all);
        Assert.Contains("NDC Portal APIs V3", all);
    }

    [Fact]
    public void A_short_file_is_one_chunk()
    {
        var result = new MarkdownChunker().Chunk("a.md", Header + "\n# Title\n\nOne short paragraph.\n", "v3");

        var chunk = Assert.Single(result.Chunks);
        Assert.Equal(0, chunk.Ordinal);
        Assert.Contains("One short paragraph.", chunk.Text);
    }

    [Fact]
    public void A_long_file_is_cut_into_several_chunks_that_respect_the_limit()
    {
        var result = new MarkdownChunker(Small).Chunk("ndc.md", Header + Body(), "v3");

        Assert.True(result.Chunks.Count > 5, $"expected several chunks, got {result.Chunks.Count}");
        Assert.All(result.Chunks, c => Assert.True(c.EstTokens <= Small.MaxTokensPerChunk,
            $"chunk {c.Ordinal} has {c.EstTokens} tokens"));
    }

    [Fact]
    public void Chunks_are_numbered_from_0_in_order_and_none_is_blank()
    {
        var result = new MarkdownChunker(Small).Chunk("ndc.md", Header + Body(), "v3");

        Assert.Equal(Enumerable.Range(0, result.Chunks.Count), result.Chunks.Select(c => c.Ordinal));
        Assert.All(result.Chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void Without_overlap_cutting_loses_no_text_and_the_chunks_joined_give_the_body()
    {
        var body = Body();

        var result = new MarkdownChunker(Small).Chunk("ndc.md", Header + body, "v3");

        Assert.Equal(WithoutWhitespace(body), WithoutWhitespace(AllText(result)));
    }

    [Fact]
    public void With_an_overlap_each_chunk_starts_with_words_repeated_from_the_end_of_the_previous_one()
    {
        var settings = Small with { OverlapTokens = 12 };

        var withOverlap = new MarkdownChunker(settings).Chunk("ndc.md", Header + Body(), "v3");
        var without = new MarkdownChunker(Small).Chunk("ndc.md", Header + Body(), "v3");

        Assert.True(withOverlap.Chunks.Count > without.Chunks.Count);
        Assert.All(withOverlap.Chunks.Skip(1), chunk =>
        {
            var firstWords = string.Join(' ', chunk.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(3));
            var previous = withOverlap.Chunks[chunk.Ordinal - 1].Text;
            Assert.Contains(firstWords, string.Join(' ', previous.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        });
        Assert.All(withOverlap.Chunks, c => Assert.True(c.EstTokens <= settings.MaxTokensPerChunk));
    }

    [Fact]
    public void With_an_overlap_no_text_is_lost_either()
    {
        var body = Body();

        var result = new MarkdownChunker(Small with { OverlapTokens = 12 }).Chunk("ndc.md", Header + body, "v3");

        AssertCovers(body, result.Chunks);
    }

    [Fact]
    public void The_same_file_twice_gives_the_same_chunks()
    {
        var first = new MarkdownChunker(Small with { OverlapTokens = 12 }).Chunk("ndc.md", Header + Body(), "v3");
        var second = new MarkdownChunker(Small with { OverlapTokens = 12 }).Chunk("ndc.md", Header + Body(), "v3");

        Assert.Equal(first.Chunks, second.Chunks);
    }

    [Fact]
    public void A_file_without_a_header_is_invalid_and_has_no_chunks_and_the_file_name_is_its_title()
    {
        var result = new MarkdownChunker().Chunk("bare.md", "# Just a heading\n\ntext\n", "v3");

        Assert.False(result.IsValid);
        Assert.Equal(["frontmatter.missing"], result.Issues.Select(i => i.Code));
        Assert.Empty(result.Chunks);
        Assert.Equal("bare.md", result.Title);
    }

    [Fact]
    public void A_version_mismatch_is_an_error_but_the_chunks_are_still_returned()
    {
        var result = new MarkdownChunker().Chunk("ndc.md", Header + Body(), "v2");

        Assert.False(result.IsValid);
        Assert.Equal(["version.mismatch"], result.Issues.Select(i => i.Code));
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public void Settings_that_cannot_work_are_refused_when_the_chunker_is_created()
    {
        Assert.Throws<InvalidOperationException>(() => new MarkdownChunker(new ChunkingSettings { OverlapTokens = 500 }));
    }

    [Theory]
    [InlineData(@"content\v2\ndc-portal-apis-v2.md", "v2")]
    [InlineData(@"content\v3\ndc-portal-apis-v3.md", "v3")]
    [InlineData(@"content\v3\whats-new-v3.md", "v3")]
    public void The_real_documentation_files_are_cut_within_the_limit_with_overlap_and_without_losing_text(string path, string version)
    {
        var text = TestFiles.ReadAllText(path);
        var settings = new ChunkingSettings();
        Assert.Equal(100, settings.OverlapTokens);

        var result = new MarkdownChunker(settings).Chunk(Path.GetFileName(path), text, version);

        Assert.Empty(result.Issues);
        Assert.StartsWith("NDC Portal APIs", result.Title);
        Assert.True(result.Chunks.Count > 5);
        Assert.All(result.Chunks, c => Assert.True(c.EstTokens <= settings.MaxTokensPerChunk,
            $"chunk {c.Ordinal} has {c.EstTokens} tokens"));
        Assert.All(result.Chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
        AssertCovers(FrontMatterReader.Read(text, version).Body, result.Chunks);
    }
}
