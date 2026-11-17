using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagSystem.Tests.Documents;

public class DocumentEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
    public async Task Without_dryRun_nothing_is_stored_yet_and_the_answer_is_501()
    {
        var response = await Post("?version=v3", Form("a.md", "# x\n"u8.ToArray()));

        await AssertProblem(response, HttpStatusCode.NotImplemented, "not_implemented");
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
