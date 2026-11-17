using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using RagSystem.Gemini.Settings;

namespace RagSystem.Gemini;

public static class GeminiServices
{
    /// <summary>
    /// Registers the Gemini settings and a Microsoft.Extensions.AI <see cref="IChatClient"/> that talks to Gemini, so the rest of
    /// the app depends on the provider-neutral interface only. A wrong setting stops the app at start. A missing API key does not:
    /// the client is built on first use and then fails with a clear message.
    /// </summary>
    public static IServiceCollection AddGemini(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = (configuration.GetSection(GeminiSettings.SectionName).Get<GeminiSettings>() ?? new GeminiSettings()).EnsureValid();
        services.AddSingleton(settings);

        // One Gemini connection (shared, thread-safe), and the chat client on top of it. The model is the default; a caller can
        // pick another per call through ChatOptions.ModelId (for example RewriteModel for the query rewrite).
        services.AddSingleton(_ =>
        {
            if (!settings.HasApiKey)
                throw new InvalidOperationException(
                    $"{GeminiSettings.SectionName}:ApiKey is not set. Set the environment variable Gemini__ApiKey (or user secrets) to your Gemini API key.");
            return new Client(
                apiKey: settings.ApiKey,
                httpOptions: new HttpOptions { BaseUrl = settings.BaseUrl, Timeout = settings.TimeoutSeconds * 1000 });
        });
        services.AddChatClient(provider => provider.GetRequiredService<Client>().AsIChatClient(settings.ChatModel));
        return services;
    }

    /// <summary>The Gemini embedding generator, behind the Microsoft.Extensions.AI interface. Throws when there is no API key.</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<GeminiSettings>();
        return provider.GetRequiredService<Client>().AsIEmbeddingGenerator(settings.EmbeddingModel, settings.EmbeddingDimensions);
    }
}
