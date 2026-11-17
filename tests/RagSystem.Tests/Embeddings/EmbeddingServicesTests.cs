using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagSystem.Embeddings;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Embeddings.Services;
using RagSystem.Embeddings.Settings;
using RagSystem.Gemini;
using RagSystem.Ollama;

namespace RagSystem.Tests.Embeddings;

public class EmbeddingServicesTests
{
    private static ServiceProvider ProviderWith(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddOllama(configuration)
            .AddGemini(configuration)
            .AddEmbeddings(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void Gemini_is_the_default_and_the_app_gets_the_neutral_generator_and_both_embedders()
    {
        using var provider = ProviderWith(new() { ["Gemini:ApiKey"] = "test-key" });

        Assert.NotNull(provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.NotNull(provider.GetRequiredService<BatchEmbedder>());
        Assert.NotNull(provider.GetRequiredService<QueryEmbedder>());
        var model = provider.GetRequiredService<EmbeddingModelInfo>();
        Assert.Equal(EmbeddingProvider.Gemini, model.Provider);
        Assert.Equal("gemini-embedding-001", model.Name);
        Assert.Equal(1024, model.Dimensions);
    }

    [Fact]
    public void With_a_key_the_real_Gemini_embedding_generator_can_be_built_for_the_configured_model()
    {
        using var provider = ProviderWith(new() { ["Gemini:ApiKey"] = "test-key" });

        using var generator = GeminiServices.CreateEmbeddingGenerator(provider);

        Assert.NotNull(generator);
    }

    [Fact]
    public void Ollama_can_be_chosen_instead_and_keeps_its_own_model_name()
    {
        using var provider = ProviderWith(new() { ["Embeddings:Provider"] = "Ollama" });

        var model = provider.GetRequiredService<EmbeddingModelInfo>();
        Assert.Equal(EmbeddingProvider.Ollama, model.Provider);
        Assert.Equal("qwen3-embedding:0.6b", model.Name);
        Assert.NotNull(provider.GetRequiredService<QueryEmbedder>());
    }

    [Fact]
    public async Task Without_an_API_key_embedding_fails_with_a_clear_fatal_error_and_the_app_still_starts()
    {
        using var provider = ProviderWith([]);

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => provider.GetRequiredService<QueryEmbedder>().EmbedAsync("hello"));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("Gemini:ApiKey", error.Message);
    }

    [Fact]
    public void A_vector_size_that_does_not_fit_the_database_stops_the_app_at_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProviderWith(new() { ["Gemini:EmbeddingDimensions"] = "768" }));

        Assert.Contains("Gemini:EmbeddingDimensions", error.Message);
    }
}
