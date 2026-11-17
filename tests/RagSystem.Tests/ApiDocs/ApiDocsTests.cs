using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RagSystem.Tests.ApiDocs;

public class ApiDocsTests(TestApp factory) : IClassFixture<TestApp>
{
    private HttpClient Client(bool enabled) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("OpenApi:Enabled", enabled.ToString())).CreateClient();

    private async Task<JsonElement> Description()
    {
        var response = await Client(enabled: true).GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task It_is_off_by_default_and_nothing_is_served()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }

    [Fact]
    public async Task When_enabled_the_swagger_page_is_served_and_points_at_the_description()
    {
        var response = await Client(enabled: true).GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());

        // The page reads which description to show from its index.js.
        var config = await Client(enabled: true).GetStringAsync("/swagger/index.js");
        Assert.Contains("/openapi/v1.json", config);
    }

    [Fact]
    public async Task The_swagger_address_without_index_html_reaches_the_page()
    {
        var response = await Client(enabled: true).GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_description_names_the_api_and_lists_the_upload()
    {
        var description = await Description();

        Assert.Equal("RagSystem", description.GetProperty("info").GetProperty("title").GetString());
        Assert.True(description.GetProperty("paths").TryGetProperty("/api/documents", out var path));
        Assert.True(path.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task The_upload_takes_a_file_as_a_multipart_form_so_the_page_shows_a_file_picker()
    {
        var upload = (await Description()).GetProperty("paths").GetProperty("/api/documents").GetProperty("post");

        var form = upload.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data");
        var file = form.GetProperty("schema").GetProperty("properties").GetProperty("file");
        Assert.Equal("string", file.GetProperty("type").GetString());
        Assert.Equal("binary", file.GetProperty("format").GetString());
    }

    [Fact]
    public async Task The_upload_lists_its_version_and_dryRun_query_parameters_and_its_answers()
    {
        var upload = (await Description()).GetProperty("paths").GetProperty("/api/documents").GetProperty("post");

        var query = upload.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query").Select(p => p.GetProperty("name").GetString()).Order();
        Assert.Equal(["dryRun", "version"], query);

        var answers = upload.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order();
        Assert.Equal(["200", "400", "415", "501"], answers);
    }

    [Fact]
    public async Task The_swagger_page_can_really_upload_a_file_through_the_same_endpoint()
    {
        // What the page's "Try it out" button sends: version and dryRun in the query, the file as a multipart form.
        var client = Client(enabled: true);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("---\nversion: v3\ntitle: T\ndocType: guide\nupdated: 2026-01-01\n---\n\n# H\n\nText.\n"u8.ToArray()), "file", "t.md" },
        };

        var response = await client.PostAsync("/api/documents?version=v3&dryRun=true", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
    }
}
