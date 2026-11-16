using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RagSystem.Ingestion;
using RagSystem.Ingestion.Markdown;

namespace RagSystem.Tests.Ingestion;

public class ChunkingSettingsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void The_values_come_from_appsettings_json()
    {
        var settings = factory.Services.GetRequiredService<ChunkingSettings>();

        Assert.Equal(new ChunkingSettings { MaxTokensPerChunk = 500, MaxTokensPerLine = 500, OverlapTokens = 50 }, settings);
    }

    [Fact]
    public void The_defaults_in_code_are_the_same_as_appsettings_json_so_the_two_cannot_drift_apart()
    {
        Assert.Equal(new ChunkingSettings(), factory.Services.GetRequiredService<ChunkingSettings>());
    }

    [Fact]
    public void A_value_in_configuration_overrides_the_file()
    {
        using var overridden = factory.WithWebHostBuilder(builder => builder.UseSetting("Chunking:OverlapTokens", "0"));

        var settings = overridden.Services.GetRequiredService<ChunkingSettings>();

        Assert.Equal(0, settings.OverlapTokens);
        Assert.Equal(500, settings.MaxTokensPerChunk);
    }

    [Fact]
    public void The_chunker_is_created_with_those_settings()
    {
        var chunker = factory.Services.GetRequiredService<MarkdownChunker>();

        var result = chunker.Chunk("a.md", "---\nversion: v3\ntitle: T\ndocType: guide\nupdated: x\n---\n# T\n\ntext\n", "v3");

        Assert.Single(result.Chunks);
    }

    [Fact]
    public void A_wrong_value_stops_the_app_at_start_with_a_clear_message()
    {
        using var wrong = factory.WithWebHostBuilder(builder => builder.UseSetting("Chunking:OverlapTokens", "500"));

        var error = Record.Exception(() => wrong.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("OverlapTokens", error.ToString());
    }

    [Theory]
    [InlineData(0, 500, 50)]
    [InlineData(500, 0, 50)]
    [InlineData(500, 500, -1)]
    [InlineData(500, 500, 500)]
    [InlineData(500, 500, 900)]
    public void Values_that_cannot_work_are_refused(int maxPerChunk, int maxPerLine, int overlap)
    {
        var settings = new ChunkingSettings { MaxTokensPerChunk = maxPerChunk, MaxTokensPerLine = maxPerLine, OverlapTokens = overlap };

        Assert.Throws<InvalidOperationException>(settings.EnsureValid);
    }

    [Fact]
    public void Working_values_are_accepted_and_returned()
    {
        var settings = new ChunkingSettings { MaxTokensPerChunk = 100, MaxTokensPerLine = 100, OverlapTokens = 0 };

        Assert.Same(settings, settings.EnsureValid());
    }
}
