using System.Text;
using RagSystem.Ingestion.Exceptions;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.OpenApi;
using RagSystem.Ingestion.Services;

namespace RagSystem.Tests.Ingestion.Services;

public class DocumentProcessorTests
{
    private const string Markdown = "---\nversion: v3\ntitle: \"NDC Portal APIs V3\"\ndocType: guide\nupdated: 2026-10-23\n---\n\n# Title\n\nOne paragraph.\n";

    private const string Swagger = """
        { "openapi": "3.0.1", "info": { "title": "OffersV3", "version": "1" },
          "paths": { "/api/V3/FareCheck": { "post": { "summary": "x", "responses": { "200": { "description": "OK" } } } } } }
        """;

    private static readonly DocumentProcessor Processor = new(new MarkdownChunker(), new OpenApiChunker());

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("guide.md")]
    [InlineData("GUIDE.MD")]
    public void A_md_file_goes_to_the_markdown_chunker(string fileName)
    {
        var result = Processor.Process(fileName, Bytes(Markdown), "v3");

        Assert.Empty(result.Issues);
        Assert.Equal("NDC Portal APIs V3", result.Title);
        Assert.Contains("One paragraph.", Assert.Single(result.Chunks).Text);
    }

    [Theory]
    [InlineData("OffersV3.swagger.json")]
    [InlineData("OffersV3.JSON")]
    public void A_json_file_goes_to_the_openapi_chunker(string fileName)
    {
        var result = Processor.Process(fileName, Bytes(Swagger), "v3");

        Assert.Empty(result.Issues);
        Assert.Equal("OffersV3", result.Title);
        Assert.StartsWith("POST /api/V3/FareCheck", Assert.Single(result.Chunks).Text);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("report.docx")]
    [InlineData("noextension")]
    [InlineData("guide.md.exe")]
    public void Any_other_type_is_refused(string fileName)
    {
        var error = Assert.Throws<UnsupportedFileTypeException>(() => Processor.Process(fileName, Bytes(Markdown), "v3"));

        Assert.Equal(fileName, error.FileName);
    }

    [Fact]
    public void A_byte_order_mark_at_the_start_of_the_file_is_ignored()
    {
        var withMark = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes(Swagger)).ToArray();

        var result = Processor.Process("a.json", withMark, "v3");

        Assert.Empty(result.Issues);
        Assert.Single(result.Chunks);
    }
}
