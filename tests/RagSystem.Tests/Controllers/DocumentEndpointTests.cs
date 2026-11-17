using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RagSystem.Tests.Controllers;

public class DocumentEndpointTests(TestApp factory) : IClassFixture<TestApp>
{
    private const int MaxTokensPerChunk = 500;

    private readonly HttpClient _client = factory.CreateClient();

    private static MultipartFormDataContent Form(string fieldName, string fileName, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, fieldName, fileName } };
    }

    private static MultipartFormDataContent Form(string fileName, byte[] content) => Form("file", fileName, content);

    private static MultipartFormDataContent RealFile(string fileName, string relativePath) =>
        Form("file", fileName, File.ReadAllBytes(TestFiles.Path(relativePath)));

    private Task<HttpResponseMessage> Post(string query, HttpContent? content) => _client.PostAsync("/api/documents" + query, content);

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Theory]
    [InlineData(@"content\v2\ndc-portal-apis-v2.md", "v2")]
    [InlineData(@"content\v3\ndc-portal-apis-v3.md", "v3")]
    [InlineData(@"content\v3\whats-new-v3.md", "v3")]
    [InlineData(@"resources\V2\OffersV2.swagger.json", "v2")]
    [InlineData(@"resources\v3\OffersV3.swagger.json", "v3")]
    public async Task The_real_files_give_a_valid_report_with_chunks_within_the_limit(string path, string version)
    {
        var response = await Post($"?version={version}&dryRun=true", RealFile(Path.GetFileName(path), path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await Json(response);
        Assert.Equal(Path.GetFileName(path), report.GetProperty("fileName").GetString());
        Assert.Equal(version, report.GetProperty("version").GetString());
        Assert.True(report.GetProperty("valid").GetBoolean());
        Assert.Equal(0, report.GetProperty("errors").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(report.GetProperty("title").GetString()));

        var chunks = report.GetProperty("chunks").EnumerateArray().ToList();
        Assert.NotEmpty(chunks);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.GetProperty("ordinal").GetInt32()));
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("text").GetString())));
        Assert.All(chunks, c => Assert.InRange(c.GetProperty("estTokens").GetInt32(), 1, MaxTokensPerChunk));
    }

    [Fact]
    public async Task The_same_file_twice_gives_the_same_chunks()
    {
        const string path = @"content\v3\whats-new-v3.md";

        var first = await (await Post("?version=v3&dryRun=true", RealFile("w.md", path))).Content.ReadAsStringAsync();
        var second = await (await Post("?version=v3&dryRun=true", RealFile("w.md", path))).Content.ReadAsStringAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task An_invalid_file_is_still_a_200_with_its_errors_and_valid_false()
    {
        var response = await Post("?version=v3&dryRun=true", Form("a.md", "# no header\n"u8.ToArray()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await Json(response);
        Assert.False(report.GetProperty("valid").GetBoolean());
        var error = report.GetProperty("errors")[0];
        Assert.Equal("frontmatter.missing", error.GetProperty("code").GetString());
        Assert.Equal(1, error.GetProperty("line").GetInt32());
        Assert.Equal(0, report.GetProperty("chunks").GetArrayLength());
    }

    [Theory]
    [InlineData(@"resources\v3\OffersV3.swagger.json", "v3", "v2", "OffersV3.swagger.json")]
    [InlineData(@"resources\V2\OffersV2.swagger.json", "v2", "v3", "OffersV2.swagger.json")]
    public async Task A_swagger_file_uploaded_for_the_other_version_reports_version_mismatch(string path, string own, string other, string name)
    {
        var report = await Json(await Post($"?version={other}&dryRun=true", RealFile(name, path)));

        Assert.False(report.GetProperty("valid").GetBoolean());
        Assert.Equal("version.mismatch", report.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.NotEqual(own, other);
    }

    [Fact]
    public async Task Only_the_file_name_is_kept_never_a_path()
    {
        var report = await Json(await Post("?version=v3&dryRun=true", Form("file", @"C:\secret\dir\notes.md", "# x\n"u8.ToArray())));

        Assert.Equal("notes.md", report.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task The_first_file_is_used_when_the_field_is_not_called_file()
    {
        var response = await Post("?version=v3&dryRun=true", Form("upload", "a.md", "# x\n"u8.ToArray()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("a.md", (await Json(response)).GetProperty("fileName").GetString());
    }

    [Theory]
    [InlineData("?dryRun=true")]
    [InlineData("?version=v4&dryRun=true")]
    [InlineData("?version=&dryRun=true")]
    public async Task A_missing_or_unknown_version_is_400_unknown_version(string query)
    {
        var response = await Post(query, Form("a.md", "# x\n"u8.ToArray()));

        await AssertProblem(response, HttpStatusCode.BadRequest, "unknown_version");
    }

    [Theory]
    [InlineData("V3")]
    [InlineData("v3")]
    public async Task The_version_is_not_case_sensitive_and_the_report_shows_it_in_lower_case(string passed)
    {
        var report = await Json(await Post($"?version={passed}&dryRun=true", RealFile("w.md", @"content\v3\whats-new-v3.md")));

        Assert.True(report.GetProperty("valid").GetBoolean());
        Assert.Equal("v3", report.GetProperty("version").GetString());
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_is_400()
    {
        var response = await Post("?version=v3&dryRun=true", JsonContent.Create(new { file = "x" }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "not_multipart");
    }

    [Fact]
    public async Task A_multipart_body_that_is_broken_is_a_400_and_not_a_server_error()
    {
        var content = new ByteArrayContent("this is not a multipart body"u8.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=abc");

        var response = await Post("?version=v3&dryRun=true", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_form_without_a_file_is_400()
    {
        var response = await Post("?version=v3&dryRun=true", new MultipartFormDataContent { { new StringContent("hello"), "note" } });

        await AssertProblem(response, HttpStatusCode.BadRequest, "no_file");
    }

    [Fact]
    public async Task A_file_of_another_type_is_415()
    {
        var response = await Post("?version=v3&dryRun=true", Form("notes.txt", "hello"u8.ToArray()));

        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "unsupported_file_type");
    }

    [Fact]
    public async Task An_invalid_file_that_is_really_uploaded_is_422_with_its_errors_and_no_chunks()
    {
        var response = await Post("?version=v3", Form("a.md", "# no header\n"u8.ToArray()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var report = await Json(response);
        Assert.False(report.GetProperty("valid").GetBoolean());
        Assert.Equal("frontmatter.missing", report.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0, report.GetProperty("chunks").GetArrayLength());
    }

    [Fact]
    public async Task A_file_with_a_header_but_no_text_is_422_document_empty()
    {
        var header = "---\nversion: v3\ntitle: T\ndocType: guide\nupdated: 2026-01-01\n---\n\n   \n"u8.ToArray();

        var response = await Post("?version=v3", Form("a.md", header));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("document.empty", (await Json(response)).GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_empty_file_is_400_no_file()
    {
        var response = await Post("?version=v3&dryRun=true", Form("a.md", []));

        await AssertProblem(response, HttpStatusCode.BadRequest, "no_file");
    }

    [Fact]
    public async Task A_file_over_10_MB_is_413_and_exactly_10_MB_is_not()
    {
        var tooBig = await Post("?version=v3&dryRun=true", Form("big.md", new byte[10 * 1024 * 1024 + 1]));
        var atLimit = await Post("?version=v3&dryRun=true", Form("big.md", new byte[10 * 1024 * 1024]));

        await AssertProblem(tooBig, HttpStatusCode.RequestEntityTooLarge, "file_too_large");
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode); // read as a file without a header: a report, not a size error
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001/chunks")]
    [InlineData("DELETE", "/00000000-0000-0000-0000-000000000001")]
    public async Task Every_document_call_needs_a_known_version(string method, string path)
    {
        foreach (var query in new[] { "", "?version=v9" })
        {
            var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/documents" + path + query));

            await AssertProblem(response, HttpStatusCode.BadRequest, "unknown_version");
        }
    }

    [Fact]
    public async Task A_document_id_that_is_not_a_guid_is_not_found_as_a_route()
    {
        var response = await _client.GetAsync("/api/documents/not-a-guid?version=v3");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await Json(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(reason, problem.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }
}
