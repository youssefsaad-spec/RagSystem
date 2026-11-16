using RagSystem.Ingestion;
using RagSystem.Ingestion.Markdown;

var builder = WebApplication.CreateBuilder(args);

// Chunking values come from the "Chunking" section of appsettings.json. A wrong value stops the app at start.
var chunking = (builder.Configuration.GetSection(ChunkingSettings.SectionName).Get<ChunkingSettings>()
                ?? new ChunkingSettings()).EnsureValid();
builder.Services.AddSingleton(chunking);
builder.Services.AddSingleton<MarkdownChunker>();

var app = builder.Build();

app.Run();

// Lets the test project start the app with WebApplicationFactory<Program>.
public partial class Program;
