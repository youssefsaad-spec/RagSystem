using RagSystem.ApiDocs;
using RagSystem.Database;
using RagSystem.Ingestion;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.OpenApi;
using RagSystem.Ollama;

var builder = WebApplication.CreateBuilder(args);

// Chunking values come from the "Chunking" section of appsettings.json. A wrong value stops the app at start.
var chunking = (builder.Configuration.GetSection(ChunkingSettings.SectionName).Get<ChunkingSettings>()
                ?? new ChunkingSettings()).EnsureValid();
builder.Services.AddSingleton(chunking);
builder.Services.AddSingleton<MarkdownChunker>();
builder.Services.AddSingleton<OpenApiChunker>();
builder.Services.AddSingleton<DocumentProcessor>();

builder.Services.AddControllers()
    // A request that is not multipart reaches the upload action, which answers 400 not_multipart (not the automatic 415).
    .ConfigureApiBehaviorOptions(options => options.SuppressConsumesConstraintForFormFileParameters = true);
builder.Services.AddApiDocs();
builder.Services.AddDatabase();
builder.Services.AddOllama(builder.Configuration);

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseApiDocs(); // /openapi/v1.json and /swagger, only when OpenApi:Enabled is true
app.MapControllers();
app.MapHealthChecks("/health"); // anonymous; checks that the database answers

app.Run();

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program;
