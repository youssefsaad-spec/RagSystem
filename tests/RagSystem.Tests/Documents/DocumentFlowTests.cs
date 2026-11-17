using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Documents.Workers;
using RagSystem.Tests.Database;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Documents;

/// <summary>Upload, worker, storage, replace, failure and delete, on a real database with a fake Ollama.</summary>
public class DocumentFlowTests
{
    private static byte[] Guide(string body = "Paragraph one explains FareCheck.\n\nParagraph two explains BookAndPay.\n", string version = "v3") =>
        System.Text.Encoding.UTF8.GetBytes($"---\nversion: {version}\ntitle: Test Guide\ndocType: guide\nupdated: 2026-01-01\n---\n\n# Title\n\n{body}");

    private static async Task<int> RevisionAsync(ApiHost host, string version)
    {
        await using var db = host.Context();
        return await db.CorpusStates.Where(s => s.Version == version).Select(s => s.Revision).SingleOrDefaultAsync();
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    // ---- Upload and store ----

    [RequiresDatabaseFact]
    public async Task An_upload_is_accepted_as_Processing_then_stored_and_Ready_with_the_chunks_of_the_dry_run()
    {
        await using var host = await ApiHost.StartAsync();
        var dryRun = await Body(await host.UploadRealFileAsync("v3", @"content\v3\whats-new-v3.md", dryRun: true));

        var response = await host.UploadRealFileAsync("v3", @"content\v3\whats-new-v3.md");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await Body(response);
        var id = ApiHost.IdOf(accepted);
        Assert.Equal("Processing", accepted.GetProperty("status").GetString());
        Assert.Equal($"/api/documents/{id}?version=v3", response.Headers.Location?.OriginalString);

        var ready = await host.WaitForStatusAsync(id, "v3", "Ready");
        var expected = dryRun.GetProperty("chunks").EnumerateArray().Select(c => c.GetProperty("text").GetString()).ToList();
        Assert.Equal(expected.Count, ready.GetProperty("chunkCount").GetInt32());
        Assert.Equal("whats-new-v3.md", ready.GetProperty("fileName").GetString());
        Assert.Equal("NDC Portal APIs V3: What's New", ready.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, ready.GetProperty("error").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, ready.GetProperty("readyAt").ValueKind);

        var chunks = await host.GetJsonAsync($"/api/documents/{id}/chunks?version=v3&take=500");
        Assert.Equal(expected, chunks.EnumerateArray().Select(c => c.GetProperty("content").GetString()));
        Assert.Equal(Enumerable.Range(0, expected.Count), chunks.EnumerateArray().Select(c => c.GetProperty("ordinal").GetInt32()));
        Assert.All(chunks.EnumerateArray(), c => Assert.Equal("qwen3-embedding:0.6b", c.GetProperty("embeddingModel").GetString()));
        Assert.False(chunks[0].TryGetProperty("embedding", out _)); // the vector is not sent
    }

    [RequiresDatabaseFact]
    public async Task The_stored_vectors_are_the_ones_ollama_returned_in_the_right_order_and_length_1()
    {
        await using var host = await ApiHost.StartAsync();
        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        await host.WaitForStatusAsync(id, "v3", "Ready");

        await using var db = host.Context();
        var vectors = await db.Chunks.Where(c => c.DocumentId == id).OrderBy(c => c.Ordinal).Select(c => c.Embedding).ToListAsync();

        Assert.NotEmpty(vectors);
        Assert.All(vectors, v => Assert.Equal(RagDbContext.EmbeddingDimensions, v.ToArray().Length));
        Assert.All(vectors, v => Assert.Equal(1.0, Math.Sqrt(v.ToArray().Sum(x => (double)x * x)), precision: 4));
    }

    [RequiresDatabaseFact]
    public async Task All_five_real_files_are_stored_and_listed_by_version()
    {
        await using var host = await ApiHost.StartAsync();
        var uploads = new (string Version, string Path)[]
        {
            ("v2", @"content\v2\ndc-portal-apis-v2.md"), ("v2", @"resources\V2\OffersV2.swagger.json"),
            ("v3", @"content\v3\ndc-portal-apis-v3.md"), ("v3", @"content\v3\whats-new-v3.md"), ("v3", @"resources\v3\OffersV3.swagger.json"),
        };

        foreach (var (version, path) in uploads)
        {
            var dry = await Body(await host.UploadRealFileAsync(version, path, dryRun: true));
            var accepted = await host.UploadRealFileAsync(version, path);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var ready = await host.WaitForStatusAsync(ApiHost.IdOf(await Body(accepted)), version, "Ready");
            Assert.Equal(dry.GetProperty("chunks").GetArrayLength(), ready.GetProperty("chunkCount").GetInt32());
        }

        var v3 = await host.GetJsonAsync("/api/documents?version=v3");
        var names = v3.EnumerateArray().Select(d => d.GetProperty("fileName").GetString()!).ToList();
        Assert.Equal(["OffersV3.swagger.json", "ndc-portal-apis-v3.md", "whats-new-v3.md"], names.Order(StringComparer.Ordinal));
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names); // listed by file name, ignoring case
        var v2 = await host.GetJsonAsync("/api/documents?version=v2");
        Assert.Equal(2, v2.GetArrayLength());
        Assert.All(v3.EnumerateArray(), d => Assert.Equal("v3", d.GetProperty("version").GetString()));
    }

    [RequiresDatabaseFact]
    public async Task The_version_is_a_hard_filter_a_document_of_another_version_is_not_found()
    {
        await using var host = await ApiHost.StartAsync();
        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        await host.WaitForStatusAsync(id, "v3", "Ready");

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{id}?version=v2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{id}/chunks?version=v2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync($"/api/documents/{id}?version=v2")).StatusCode);
        Assert.Equal(0, (await host.GetJsonAsync("/api/documents?version=v2")).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"/api/documents/{id}?version=V3")).StatusCode); // any case
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_document_is_404_problem_json()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync($"/api/documents/{Guid.NewGuid()}?version=v3");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("document_not_found", (await Body(response)).GetProperty("reason").GetString());
    }

    // ---- Paging the chunks ----

    [RequiresDatabaseFact]
    public async Task Chunks_are_paged_with_skip_and_take_take_is_limited_to_500_and_a_bad_skip_counts_as_0()
    {
        await using var host = await ApiHost.StartAsync();
        var id = ApiHost.IdOf(await Body(await host.UploadRealFileAsync("v3", @"content\v3\ndc-portal-apis-v3.md")));
        var ready = await host.WaitForStatusAsync(id, "v3", "Ready");
        var total = ready.GetProperty("chunkCount").GetInt32();
        Assert.True(total > 100);

        int[] Ordinals(JsonElement chunks) => chunks.EnumerateArray().Select(c => c.GetProperty("ordinal").GetInt32()).ToArray();
        var url = $"/api/documents/{id}/chunks?version=v3";

        Assert.Equal(100, (await host.GetJsonAsync(url)).GetArrayLength()); // default take
        Assert.Equal(new[] { 10, 11, 12 }, Ordinals(await host.GetJsonAsync(url + "&skip=10&take=3")));
        Assert.Equal(new[] { 0 }, Ordinals(await host.GetJsonAsync(url + "&take=0")));      // at least 1
        Assert.Equal(new[] { 0, 1 }, Ordinals(await host.GetJsonAsync(url + "&skip=-5&take=2"))); // negative skip is 0
        Assert.Equal(total, (await host.GetJsonAsync(url + "&take=9999")).GetArrayLength()); // at most 500, and the file has fewer
        Assert.Equal(0, (await host.GetJsonAsync(url + $"&skip={total}")).GetArrayLength());
    }

    // ---- Replace ----

    [RequiresDatabaseFact]
    public async Task Uploading_the_same_file_again_replaces_the_document_and_its_chunks()
    {
        await using var host = await ApiHost.StartAsync();
        var first = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide("Old text only.\n"))));
        await host.WaitForStatusAsync(first, "v3", "Ready");
        await using var db = host.Context();
        var oldChunkIds = await db.Chunks.Where(c => c.DocumentId == first).Select(c => c.Id).ToListAsync();

        var second = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide("New text, now longer.\n\nAnd a second paragraph that is new too.\n"))));
        await host.WaitForStatusAsync(second, "v3", "Ready");

        Assert.Equal(first, second); // same document
        Assert.Equal(1, (await host.GetJsonAsync("/api/documents?version=v3")).GetArrayLength());
        var chunks = await host.GetJsonAsync($"/api/documents/{first}/chunks?version=v3");
        var text = string.Join(" ", chunks.EnumerateArray().Select(c => c.GetProperty("content").GetString()));
        Assert.Contains("New text", text);
        Assert.DoesNotContain("Old text", text);
        Assert.Empty(await db.Chunks.Where(c => oldChunkIds.Contains(c.Id)).ToListAsync());
    }

    [RequiresDatabaseFact]
    public async Task A_second_upload_while_the_first_is_processing_is_409_and_it_works_again_once_the_first_is_done()
    {
        var gate = new TaskCompletionSource();
        await using var host = await ApiHost.StartAsync(async (request, _) =>
        {
            await gate.Task;
            return StubHandler.EmbeddingsForEveryText(request);
        });
        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));

        var busy = await host.UploadAsync("v3", "g.md", Guide());
        var otherFile = await host.UploadAsync("v3", "other.md", Guide());

        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        var problem = await Body(busy);
        Assert.Equal("document_processing", problem.GetProperty("reason").GetString());
        Assert.Contains(id.ToString(), problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Accepted, otherFile.StatusCode); // another file is fine

        gate.SetResult();
        await host.WaitForStatusAsync(id, "v3", "Ready");
        Assert.Equal(HttpStatusCode.Accepted, (await host.UploadAsync("v3", "g.md", Guide())).StatusCode);
    }

    // ---- Failure ----

    [RequiresDatabaseFact]
    public async Task When_ollama_fails_for_good_the_document_is_Failed_with_the_error_and_stores_no_chunks()
    {
        await using var host = await ApiHost.StartAsync((_, _) =>
            Task.FromResult(StubHandler.Reply(HttpStatusCode.NotFound, "{\"error\":\"model \\\"qwen3-embedding:0.6b\\\" not found, try pulling it first\"}")));

        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        var failed = await host.WaitForStatusAsync(id, "v3", "Failed");

        Assert.Contains("not found", failed.GetProperty("error").GetString());
        Assert.Equal(0, failed.GetProperty("chunkCount").GetInt32());
        await using var db = host.Context();
        Assert.Empty(await db.Chunks.ToListAsync());
        Assert.Equal(0, await RevisionAsync(host, "v3")); // nothing changed, so the revision did not move
    }

    [RequiresDatabaseFact]
    public async Task A_failed_replacement_keeps_the_old_chunks_and_a_later_upload_can_try_again()
    {
        var fail = false;
        await using var host = await ApiHost.StartAsync((request, _) =>
            Task.FromResult(fail ? StubHandler.Reply(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}") : StubHandler.EmbeddingsForEveryText(request)));
        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide("Old text only.\n"))));
        await host.WaitForStatusAsync(id, "v3", "Ready");

        fail = true;
        await host.UploadAsync("v3", "g.md", Guide("New text.\n"));
        var failed = await host.WaitForStatusAsync(id, "v3", "Failed");

        Assert.Equal(1, failed.GetProperty("chunkCount").GetInt32()); // the old count is still true
        var chunks = await host.GetJsonAsync($"/api/documents/{id}/chunks?version=v3");
        Assert.Contains("Old text only", chunks[0].GetProperty("content").GetString());
        Assert.Equal(1, await RevisionAsync(host, "v3"));

        fail = false; // Failed is not Processing, so uploading again is allowed
        Assert.Equal(HttpStatusCode.Accepted, (await host.UploadAsync("v3", "g.md", Guide("Third text.\n"))).StatusCode);
        var ready = await host.WaitForStatusAsync(id, "v3", "Ready");
        Assert.Equal(JsonValueKind.Null, ready.GetProperty("error").ValueKind);
        Assert.Contains("Third text", (await host.GetJsonAsync($"/api/documents/{id}/chunks?version=v3"))[0].GetProperty("content").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_transient_failure_is_retried_and_the_document_still_becomes_Ready()
    {
        var calls = 0;
        await using var host = await ApiHost.StartAsync((request, _) =>
            Task.FromResult(++calls == 1 ? StubHandler.Reply(HttpStatusCode.ServiceUnavailable, "{}") : StubHandler.EmbeddingsForEveryText(request)));

        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));

        await host.WaitForStatusAsync(id, "v3", "Ready"); // the retry waits 2 s, which is inside the 20 s limit
        Assert.True(calls >= 2);
    }

    [RequiresDatabaseFact]
    public async Task A_long_error_text_is_cut_to_2000_characters()
    {
        await using var host = await ApiHost.StartAsync((_, _) =>
            Task.FromResult(StubHandler.Reply(HttpStatusCode.BadRequest, "{\"error\":\"" + new string('x', 5000) + "\"}")));

        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        var failed = await host.WaitForStatusAsync(id, "v3", "Failed");

        Assert.Equal(IngestionJobRunner.MaxErrorLength, failed.GetProperty("error").GetString()!.Length);
    }

    // ---- Restart ----

    [RequiresDatabaseFact]
    public async Task At_start_a_document_still_Processing_is_marked_Failed_as_interrupted()
    {
        var id = Guid.NewGuid();
        await using var host = await ApiHost.StartAsync(prepare: async db =>
        {
            db.Documents.Add(new Document { Id = id, Version = "v3", FileName = "left-over.md", Title = "T", Status = DocumentStatus.Processing });
            db.Documents.Add(new Document { Id = Guid.NewGuid(), Version = "v3", FileName = "done.md", Title = "T", Status = DocumentStatus.Ready, ChunkCount = 3 });
            await db.SaveChangesAsync();
        });

        var failed = await host.WaitForStatusAsync(id, "v3", "Failed");

        Assert.Equal(IngestionWorker.InterruptedMessage, failed.GetProperty("error").GetString());
        var list = await host.GetJsonAsync("/api/documents?version=v3");
        Assert.Equal("Ready", list.EnumerateArray().Single(d => d.GetProperty("fileName").GetString() == "done.md").GetProperty("status").GetString());
    }

    // ---- Delete and the revision ----

    [RequiresDatabaseFact]
    public async Task Delete_removes_the_document_and_its_chunks_and_a_second_delete_is_404()
    {
        await using var host = await ApiHost.StartAsync();
        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        var keep = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "keep.md", Guide())));
        await host.WaitForStatusAsync(id, "v3", "Ready");
        await host.WaitForStatusAsync(keep, "v3", "Ready");

        var deleted = await host.Client.DeleteAsync($"/api/documents/{id}?version=v3");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/documents/{id}?version=v3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync($"/api/documents/{id}?version=v3")).StatusCode);
        await using var db = host.Context();
        Assert.Empty(await db.Chunks.Where(c => c.DocumentId == id).ToListAsync());
        Assert.NotEmpty(await db.Chunks.Where(c => c.DocumentId == keep).ToListAsync());
    }

    [RequiresDatabaseFact]
    public async Task The_revision_of_a_version_goes_up_on_every_store_and_delete_and_only_for_that_version()
    {
        await using var host = await ApiHost.StartAsync();
        Assert.Equal(0, await RevisionAsync(host, "v3"));

        var id = ApiHost.IdOf(await Body(await host.UploadAsync("v3", "g.md", Guide())));
        await host.WaitForStatusAsync(id, "v3", "Ready");
        Assert.Equal(1, await RevisionAsync(host, "v3"));

        await host.UploadAsync("v3", "g.md", Guide("Changed.\n"));
        await host.WaitForStatusAsync(id, "v3", "Ready");
        Assert.Equal(2, await RevisionAsync(host, "v3"));

        await host.Client.DeleteAsync($"/api/documents/{id}?version=v3");
        Assert.Equal(3, await RevisionAsync(host, "v3"));

        await host.Client.DeleteAsync($"/api/documents/{id}?version=v3"); // 404: nothing changed
        Assert.Equal(3, await RevisionAsync(host, "v3"));
        Assert.Equal(0, await RevisionAsync(host, "v2"));
    }

    [RequiresDatabaseFact]
    public async Task A_valid_dry_run_and_an_invalid_upload_touch_nothing_in_the_database()
    {
        await using var host = await ApiHost.StartAsync();

        await host.UploadAsync("v3", "g.md", Guide(), dryRun: true);
        var invalid = await host.UploadAsync("v3", "bad.md", System.Text.Encoding.UTF8.GetBytes("no header"));
        var wrongVersion = await host.UploadAsync("v3", "v2.md", Guide(version: "v2"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongVersion.StatusCode);
        Assert.Equal("version.mismatch", (await Body(wrongVersion)).GetProperty("errors")[0].GetProperty("code").GetString());
        await using var db = host.Context();
        Assert.Empty(await db.Documents.ToListAsync());
        Assert.Empty(await db.CorpusStates.ToListAsync());
    }
}
