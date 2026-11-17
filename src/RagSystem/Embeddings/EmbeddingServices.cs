using Microsoft.Extensions.AI;
using RagSystem.Embeddings.Services;
using RagSystem.Embeddings.Settings;
using RagSystem.Gemini;
using RagSystem.Gemini.Settings;
using RagSystem.Ollama;
using RagSystem.Ollama.Settings;

namespace RagSystem.Embeddings;

public static class EmbeddingServices
{
    /// <summary>
    /// Registers the one <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> the app uses, chosen by
    /// <c>Embeddings:Provider</c> (Gemini by default, or Ollama), and the two classes that use it. Needs <c>AddGemini</c> and
    /// <c>AddOllama</c> to have run (they register the settings and clients; neither calls its service until it is used).
    /// </summary>
    public static IServiceCollection AddEmbeddings(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(EmbeddingSettings.SectionName).Get<EmbeddingSettings>() ?? new EmbeddingSettings();
        services.AddSingleton(settings);

        switch (settings.Provider)
        {
            case EmbeddingProvider.Gemini:
                var gemini = ReadGemini(configuration);
                services.AddSingleton(new EmbeddingModelInfo(settings.Provider, gemini.EmbeddingModel, gemini.EmbeddingDimensions));
                services.AddEmbeddingGenerator<string, Embedding<float>>(
                    provider => new GuardedEmbeddingGenerator("Gemini", gemini.EmbeddingDimensions,
                        () => GeminiServices.CreateEmbeddingGenerator(provider)),
                    ServiceLifetime.Transient);
                break;

            case EmbeddingProvider.Ollama:
                var ollama = (configuration.GetSection(OllamaSettings.SectionName).Get<OllamaSettings>() ?? new OllamaSettings()).EnsureValid();
                services.AddSingleton(new EmbeddingModelInfo(settings.Provider, ollama.EmbeddingModel, ollama.EmbeddingDimensions));
                services.AddEmbeddingGenerator<string, Embedding<float>>(
                    provider => new GuardedEmbeddingGenerator("Ollama", ollama.EmbeddingDimensions,
                        () => OllamaServices.CreateEmbeddingGenerator(provider)),
                    ServiceLifetime.Transient);
                break;

            default:
                throw new InvalidOperationException($"{EmbeddingSettings.SectionName}:Provider '{settings.Provider}' is not supported.");
        }

        services.AddTransient<BatchEmbedder>();
        services.AddTransient<QueryEmbedder>();
        return services;
    }

    private static GeminiSettings ReadGemini(IConfiguration configuration) =>
        (configuration.GetSection(GeminiSettings.SectionName).Get<GeminiSettings>() ?? new GeminiSettings()).EnsureValid();
}
