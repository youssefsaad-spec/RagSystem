using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RagSystem.Database;
using RagSystem.Ollama.Services;
using RagSystem.Tests.Database;
using RagSystem.Tests.Ollama;

namespace RagSystem.Tests.Documents;

/// <summary>
/// The whole app on a throw-away Postgres database, with a fake Ollama: what the document endpoints, the queue and the
/// worker do together. Needs <c>RAGSYSTEM_TEST_DB</c> (see <see cref="TestDatabase"/>).
/// </summary>
public sealed class ApiHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly TestDatabase _database;

    private ApiHost(WebApplicationFactory<Program> app, TestDatabase database, HttpClient client)
    {
        _app = app;
        _database = database;
        Client = client;
    }

    public HttpClient Client { get; }

    public string ConnectionString => _database.ConnectionString;

    /// <param name="ollama">The fake Ollama. By default it answers every text with a valid vector.</param>
    /// <param name="prepare">Runs on the migrated database before the app starts (to put rows in it).</param>
    public static async Task<ApiHost> StartAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? ollama = null,
        Func<RagDbContext, Task>? prepare = null)
    {
        var database = await TestDatabase.CreateAsync();
        if (prepare is not null)
        {
            await using var context = NewContext(database.ConnectionString);
            await context.Database.MigrateAsync();
            await prepare(context);
        }

        var stub = new StubHandler(ollama ?? ((request, _) => Task.FromResult(StubHandler.EmbeddingsForEveryText(request))));
        var app = new TestApp().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Db", database.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Embeddings:Provider", "Ollama"); // the fake Ollama below stands in for the embedding service
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<OllamaEmbeddingClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
                // One test app per test means many EF service providers in one process (see TestContexts).
                services.ConfigureDbContext<RagDbContext>(options =>
                    options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
            });
        });
        return new ApiHost(app, database, app.CreateClient());
    }

    public static RagDbContext NewContext(string connectionString) =>
        new(TestContexts.Options(connectionString));

    public RagDbContext Context() => NewContext(ConnectionString);

    public async Task<HttpResponseMessage> UploadAsync(string version, string fileName, byte[] content, bool dryRun = false)
    {
        var file = new ByteArrayContent(content);
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await Client.PostAsync($"/api/documents?version={version}" + (dryRun ? "&dryRun=true" : ""), form);
    }

    public Task<HttpResponseMessage> UploadRealFileAsync(string version, string relativePath, bool dryRun = false) =>
        UploadAsync(version, Path.GetFileName(relativePath), File.ReadAllBytes(TestFiles.Path(relativePath)), dryRun);

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Waits (at most 20 s) until the document has this status, and returns the document.</summary>
    public async Task<JsonElement> WaitForStatusAsync(Guid id, string version, string status)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        JsonElement document;
        do
        {
            document = await GetJsonAsync($"/api/documents/{id}?version={version}");
            if (document.GetProperty("status").GetString() == status)
                return document;
            await Task.Delay(50);
        }
        while (DateTime.UtcNow < until);
        throw new TimeoutException($"The document stayed '{document.GetProperty("status").GetString()}', not '{status}'.");
    }

    public static Guid IdOf(JsonElement accepted) => accepted.GetProperty("id").GetGuid();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }
}
