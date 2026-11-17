using System.Text.RegularExpressions;
using RagSystem.Ingestion.Models;
using RagSystem.Ingestion.OpenApi;
using RagSystem.Ingestion.Settings;

namespace RagSystem.Tests.Ingestion.OpenApi;

public partial class OpenApiChunkerTests
{
    private const string V3Swagger = @"resources\v3\OffersV3.swagger.json";
    private const string V2Swagger = @"resources\V2\OffersV2.swagger.json";

    /// <summary>A header line such as "POST /api/V3/FareCheck".</summary>
    [GeneratedRegex(@"^(GET|POST|PUT|DELETE|PATCH) /\S+", RegexOptions.Multiline)]
    private static partial Regex OperationHeader();

    private static string Spec(string title = "OffersV3", string paths = "", string schemas = "") => $$"""
        {
          "openapi": "3.0.1",
          "info": { "title": "{{title}}", "version": "1.0" },
          "paths": { {{paths}} },
          "components": { "schemas": { {{schemas}} } }
        }
        """;

    private const string ShortPath = """
        "/api/V3/FareCheck": { "post": { "summary": "Revalidates the price", "responses": { "200": { "description": "OK" } } } }
        """;

    private const string ShortSchema = """
        "Passenger": { "type": "object", "properties": { "name": { "type": "string" } } }
        """;

    private static IngestionResult Chunk(string json, string version = "v3", ChunkingSettings? settings = null) =>
        new OpenApiChunker(settings ?? new ChunkingSettings()).Chunk("OffersV3.swagger.json", json, version);

    private static int OperationCount(IngestionResult result) =>
        result.Chunks.Select(c => OperationHeader().Match(c.Text)).Where(m => m.Success).Select(m => m.Value).Distinct().Count();

    [Fact]
    public void A_short_operation_is_one_chunk_that_starts_with_its_method_and_route()
    {
        var result = Chunk(Spec(paths: ShortPath));

        Assert.Empty(result.Issues);
        var chunk = Assert.Single(result.Chunks);
        Assert.StartsWith("POST /api/V3/FareCheck\n", chunk.Text);
        Assert.Contains("Revalidates the price", chunk.Text);
    }

    [Fact]
    public void A_schema_is_its_own_chunk_that_starts_with_its_name()
    {
        var result = Chunk(Spec(paths: ShortPath, schemas: ShortSchema));

        Assert.Equal(2, result.Chunks.Count);
        Assert.StartsWith("Schema Passenger\n", result.Chunks[1].Text);
        Assert.Contains("\"name\"", result.Chunks[1].Text);
    }

    [Fact]
    public void References_are_kept_short_and_not_expanded()
    {
        var paths = """
            "/api/V3/Book": { "post": { "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Passenger" } } } },
                                        "responses": { "200": { "description": "OK" } } } }
            """;

        var result = Chunk(Spec(paths: paths, schemas: ShortSchema));

        var operation = result.Chunks[0].Text;
        Assert.Contains("#/components/schemas/Passenger", operation);
        Assert.DoesNotContain("\"name\"", operation);
    }

    [Fact]
    public void The_title_is_info_title_or_the_file_name_when_there_is_none()
    {
        Assert.Equal("OffersV3", Chunk(Spec(title: "OffersV3", paths: ShortPath)).Title);
        Assert.Equal("OffersV3.swagger.json", Chunk(Spec(title: "", paths: ShortPath)).Title);
    }

    [Fact]
    public void A_long_operation_is_cut_and_every_piece_starts_with_the_same_header_within_the_limit()
    {
        var properties = string.Join(",", Enumerable.Range(1, 60).Select(i => $"\"field{i}\": {{ \"type\": \"string\", \"description\": \"Description of field number {i}.\" }}"));
        var paths = $$"""
            "/api/V3/Big": { "post": { "summary": "Big", "requestBody": { "content": { "application/json": { "schema": { "type": "object", "properties": { {{properties}} } } } } },
                                       "responses": { "200": { "description": "OK" } } } }
            """;
        var settings = new ChunkingSettings { MaxTokensPerChunk = 120, MaxTokensPerLine = 120, OverlapTokens = 0 };

        var result = Chunk(Spec(paths: paths), settings: settings);

        Assert.True(result.Chunks.Count > 3, $"expected several pieces, got {result.Chunks.Count}");
        Assert.All(result.Chunks, c => Assert.StartsWith("POST /api/V3/Big\n", c.Text));
        Assert.All(result.Chunks, c => Assert.True(c.EstTokens <= settings.MaxTokensPerChunk, $"chunk {c.Ordinal} has {c.EstTokens} tokens"));
        Assert.Equal(Enumerable.Range(0, result.Chunks.Count), result.Chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void Chunks_follow_the_file_order_operations_first_then_schemas()
    {
        var paths = ShortPath + "," + ShortPath.Replace("FareCheck", "Cancel");

        var result = Chunk(Spec(paths: paths, schemas: ShortSchema));

        Assert.Equal(
            ["POST /api/V3/FareCheck", "POST /api/V3/Cancel", "Schema Passenger"],
            result.Chunks.Select(c => c.Text.Split('\n')[0]));
    }

    [Fact]
    public void A_path_of_another_version_is_a_version_mismatch_but_the_chunks_are_still_returned()
    {
        var result = Chunk(Spec(paths: ShortPath), version: "v2");

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("version.mismatch", issue.Code);
        Assert.Contains("/api/V3/FareCheck", issue.Message);
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public void A_path_that_is_not_under_api_v_n_is_a_version_mismatch()
    {
        var result = Chunk(Spec(paths: ShortPath.Replace("/api/V3/", "/health/")));

        Assert.Equal(["version.mismatch"], result.Issues.Select(i => i.Code));
    }

    [Fact]
    public void A_file_with_no_paths_is_an_error()
    {
        var result = Chunk(Spec(schemas: ShortSchema));

        Assert.Equal(["openapi.empty"], result.Issues.Select(i => i.Code));
    }

    [Theory]
    [InlineData("this is not json at all")]
    [InlineData("")]
    [InlineData("{ \"hello\": ")]
    public void A_file_that_is_not_an_openapi_document_is_unreadable_and_has_no_chunks(string text)
    {
        var result = Chunk(text);

        Assert.False(result.IsValid);
        Assert.Equal("openapi.unreadable", result.Issues[0].Code);
        Assert.Empty(result.Chunks);
        Assert.Equal("OffersV3.swagger.json", result.Title);
    }

    [Fact]
    public void Settings_that_cannot_work_are_refused_when_the_chunker_is_created()
    {
        Assert.Throws<InvalidOperationException>(() => new OpenApiChunker(new ChunkingSettings { MaxTokensPerChunk = 0 }));
    }

    [Theory]
    [InlineData(V3Swagger, "v3", "v2", 18)]
    [InlineData(V2Swagger, "v2", "v3", 16)]
    public void The_real_swagger_files_parse_pass_their_own_version_and_fail_the_other(string path, string own, string other, int operations)
    {
        var json = TestFiles.ReadAllText(path);
        var settings = new ChunkingSettings();

        var result = Chunk(json, own);

        Assert.Empty(result.Issues);
        Assert.Equal(operations, OperationCount(result));
        Assert.Contains(result.Chunks, c => c.Text.StartsWith("Schema ", StringComparison.Ordinal));
        Assert.All(result.Chunks, c => Assert.True(c.EstTokens <= settings.MaxTokensPerChunk, $"chunk {c.Ordinal} has {c.EstTokens} tokens"));
        Assert.All(result.Chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));

        var wrong = Chunk(json, other);
        Assert.Equal(["version.mismatch"], wrong.Issues.Select(i => i.Code));
    }

    [Fact]
    public void The_real_v3_file_gives_a_title_and_the_same_chunks_every_time()
    {
        var json = TestFiles.ReadAllText(V3Swagger);

        var first = Chunk(json);
        var second = Chunk(json);

        Assert.Equal("OffersV3", first.Title);
        Assert.Equal(first.Chunks, second.Chunks);
    }
}
