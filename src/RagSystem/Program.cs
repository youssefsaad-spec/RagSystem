using RagSystem.Documents;
using RagSystem.Ingestion;
using RagSystem.Ingestion.Markdown;
using RagSystem.Ingestion.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Chunking values come from the "Chunking" section of appsettings.json. A wrong value stops the app at start.
var chunking = (builder.Configuration.GetSection(ChunkingSettings.SectionName).Get<ChunkingSettings>()
                ?? new ChunkingSettings()).EnsureValid();
builder.Services.AddSingleton(chunking);
builder.Services.AddSingleton<MarkdownChunker>();
builder.Services.AddSingleton<OpenApiChunker>();
builder.Services.AddSingleton<DocumentProcessor>();

var app = builder.Build();

app.MapDocumentEndpoints();

app.Run();

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program;
