namespace RagSystem.Ollama;

public static class OllamaServices
{
    /// <summary>
    /// Registers the Ollama settings and the embedding client. A wrong value (for example a size other than the
    /// database column's) stops the app at start.
    /// </summary>
    public static IServiceCollection AddOllama(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = (configuration.GetSection(OllamaSettings.SectionName).Get<OllamaSettings>() ?? new OllamaSettings()).EnsureValid();
        services.AddSingleton(settings);
        services.AddHttpClient<OllamaEmbeddingClient>(http =>
        {
            http.BaseAddress = new Uri(settings.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });
        services.AddTransient<BatchEmbedder>();
        return services;
    }
}
