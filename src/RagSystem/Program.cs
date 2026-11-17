using FluentValidation;
using RagSystem.ApiDocs;
using RagSystem.Database;
using RagSystem.Documents.Services;
using RagSystem.Documents.Workers;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.OpenApi;
using RagSystem.Ingestion.Services;
using RagSystem.Ingestion.Settings;
using RagSystem.Embeddings;
using RagSystem.Gemini;
using RagSystem.Ollama;
using RagSystem.Retrieval;
using RagSystem.Validation.Validators;

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
builder.Services.AddValidatorsFromAssemblyContaining<VersionRequestValidator>(); // every validator in the Validation folder
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IngestionQueue>();
builder.Services.AddScoped<DocumentSubmitter>();
builder.Services.AddScoped<DocumentQueries>();
builder.Services.AddScoped<DocumentDeleter>();
builder.Services.AddScoped<IngestionJobRunner>();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddDatabase();
builder.Services.AddOllama(builder.Configuration);
builder.Services.AddGemini(builder.Configuration);
builder.Services.AddEmbeddings(builder.Configuration);
builder.Services.AddRetrieval(builder.Configuration);

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseApiDocs(); // /openapi/v1.json and /swagger, only when OpenApi:Enabled is true
app.MapControllers();
app.MapHealthChecks("/health"); // anonymous; checks that the database answers

app.Run();

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program;
