using RagSystem.Ingestion;
using RagSystem.Ingestion.Markdown;

namespace RagSystem.Tests.Ingestion.Markdown;

public class FrontMatterReaderTests
{
    private const string Valid = """
        ---
        version: v3
        title: "NDC Portal APIs V3"
        docType: guide
        updated: 2026-10-23
        source: "NDC APIs(V3) Documentation V3.0.docx"
        ---

        # NDC Portal APIs V3
        """;

    private static string[] Codes(FrontMatterResult result) => result.Issues.Select(i => i.Code).ToArray();

    [Fact]
    public void Reads_all_fields_of_a_valid_header()
    {
        var result = FrontMatterReader.Read(Valid, "v3");

        Assert.Empty(result.Issues);
        Assert.Equal(
            new FrontMatter("v3", "NDC Portal APIs V3", "guide", "2026-10-23", "NDC APIs(V3) Documentation V3.0.docx"),
            result.FrontMatter);
    }

    [Fact]
    public void Returns_the_body_after_the_header_and_the_line_where_it_starts()
    {
        var result = FrontMatterReader.Read(Valid, "v3");

        // The header uses lines 1 to 7, so the (blank) first body line is line 8.
        Assert.Equal(8, result.BodyStartLine);
        Assert.Equal("\n# NDC Portal APIs V3", result.Body);
    }

    [Fact]
    public void Source_is_optional()
    {
        var result = FrontMatterReader.Read("---\nversion: v2\ntitle: T\ndocType: guide\nupdated: 2026-01-01\n---\n# T", "v2");

        Assert.Empty(result.Issues);
        Assert.Null(result.FrontMatter!.Source);
    }

    [Fact]
    public void Only_the_first_colon_splits_a_line_so_a_title_may_contain_colons()
    {
        var result = FrontMatterReader.Read(
            "---\nversion: v3\ntitle: NDC Portal APIs V3: What's New\ndocType: whats-new\nupdated: 2026-10-27\n---\n", "v3");

        Assert.Empty(result.Issues);
        Assert.Equal("NDC Portal APIs V3: What's New", result.FrontMatter!.Title);
    }

    [Theory]
    [InlineData("title: \"Quoted\"", "Quoted")]
    [InlineData("title: 'Single'", "Single")]
    [InlineData("title:    spaced out   ", "spaced out")]
    [InlineData("title: \"keeps 'inner' quotes\"", "keeps 'inner' quotes")]
    public void Removes_matching_quotes_and_surrounding_spaces(string titleLine, string expectedTitle)
    {
        var result = FrontMatterReader.Read($"---\nversion: v3\n{titleLine}\ndocType: guide\nupdated: x\n---\n", "v3");

        Assert.Empty(result.Issues);
        Assert.Equal(expectedTitle, result.FrontMatter!.Title);
    }

    [Fact]
    public void Keys_are_matched_in_any_letter_case_and_the_version_is_stored_in_lower_case()
    {
        var result = FrontMatterReader.Read("---\nVersion: V3\nTITLE: T\nDOCTYPE: guide\nUpdated: x\n---\n", "v3");

        Assert.Empty(result.Issues);
        Assert.Equal("v3", result.FrontMatter!.Version);
    }

    [Fact]
    public void Blank_lines_and_comment_lines_are_skipped()
    {
        var result = FrontMatterReader.Read(
            "---\n# a comment\nversion: v3\n\ntitle: T\ndocType: guide\nupdated: x\n---\n", "v3");

        Assert.Empty(result.Issues);
        Assert.NotNull(result.FrontMatter);
    }

    [Fact]
    public void Handles_windows_line_ends_and_a_byte_order_mark()
    {
        var text = "﻿---\r\nversion: v3\r\ntitle: T\r\ndocType: guide\r\nupdated: x\r\n---\r\n# T\r\n";

        var result = FrontMatterReader.Read(text, "v3");

        Assert.Empty(result.Issues);
        Assert.Equal("T", result.FrontMatter!.Title);
        Assert.Equal("# T\n", result.Body);
        Assert.Equal(7, result.BodyStartLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# A heading, no header")]
    [InlineData("version: v3\n---\n")]
    public void A_file_without_a_header_is_an_error_and_the_whole_text_is_the_body(string text)
    {
        var result = FrontMatterReader.Read(text, "v3");

        Assert.Equal(["frontmatter.missing"], Codes(result));
        Assert.Null(result.FrontMatter);
        Assert.Equal(text, result.Body);
        Assert.Equal(1, result.BodyStartLine);
    }

    [Fact]
    public void A_header_without_a_closing_line_is_an_error()
    {
        var result = FrontMatterReader.Read("---\nversion: v3\ntitle: T\n# Heading", "v3");

        Assert.Equal(["frontmatter.unterminated"], Codes(result));
        Assert.Null(result.FrontMatter);
    }

    [Fact]
    public void Each_missing_field_is_reported()
    {
        var result = FrontMatterReader.Read("---\nversion: v3\n---\n# T", "v3");

        Assert.Equal(["frontmatter.field", "frontmatter.field", "frontmatter.field"], Codes(result));
        Assert.Contains("'title'", result.Issues[0].Message);
        Assert.Contains("'docType'", result.Issues[1].Message);
        Assert.Contains("'updated'", result.Issues[2].Message);
        Assert.Null(result.FrontMatter);
    }

    [Fact]
    public void An_empty_value_counts_as_missing()
    {
        var result = FrontMatterReader.Read("---\nversion: v3\ntitle:\ndocType: guide\nupdated: x\n---\n", "v3");

        Assert.Equal(["frontmatter.field"], Codes(result));
        Assert.Contains("'title'", result.Issues[0].Message);
    }

    [Fact]
    public void A_line_that_is_not_key_and_value_is_reported_with_its_line_number()
    {
        var result = FrontMatterReader.Read("---\nversion: v3\nthis is not a pair\ntitle: T\n---\n", "v3");

        var issue = Assert.Single(result.Issues);
        Assert.Equal("frontmatter.invalid", issue.Code);
        Assert.Equal(3, issue.Line);
        Assert.Null(result.FrontMatter);
    }

    [Fact]
    public void An_unknown_docType_is_an_error_but_the_header_is_still_returned()
    {
        var result = FrontMatterReader.Read("---\nversion: v3\ntitle: T\ndocType: manual\nupdated: x\n---\n", "v3");

        Assert.Equal(["frontmatter.docType"], Codes(result));
        Assert.Contains("guide, whats-new", result.Issues[0].Message);
        Assert.NotNull(result.FrontMatter);
    }

    [Fact]
    public void A_version_that_differs_from_the_upload_version_is_an_error()
    {
        var result = FrontMatterReader.Read(Valid, "v2");

        Assert.Equal(["version.mismatch"], Codes(result));
        Assert.Contains("'v3'", result.Issues[0].Message);
        Assert.Contains("'v2'", result.Issues[0].Message);
    }

    [Fact]
    public void The_version_comparison_ignores_letter_case()
    {
        var result = FrontMatterReader.Read("---\nversion: V3\ntitle: T\ndocType: guide\nupdated: x\n---\n", "v3");

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Several_problems_are_reported_together()
    {
        var result = FrontMatterReader.Read("---\nversion: v2\ntitle: T\ndocType: manual\nupdated: x\n---\n", "v3");

        Assert.Equal(["frontmatter.docType", "version.mismatch"], Codes(result));
    }

    [Theory]
    [InlineData(@"content\v2\ndc-portal-apis-v2.md", "v2", "guide")]
    [InlineData(@"content\v3\ndc-portal-apis-v3.md", "v3", "guide")]
    [InlineData(@"content\v3\whats-new-v3.md", "v3", "whats-new")]
    public void Reads_the_real_documentation_files_without_issues(string path, string version, string docType)
    {
        var result = FrontMatterReader.Read(TestFiles.ReadAllText(path), version);

        Assert.Empty(result.Issues);
        Assert.Equal(version, result.FrontMatter!.Version);
        Assert.Equal(docType, result.FrontMatter.DocType);
        Assert.StartsWith("NDC Portal APIs", result.FrontMatter.Title);
        Assert.StartsWith("\n# NDC Portal APIs", result.Body.Replace("\r", ""));
    }
}
