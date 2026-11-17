using RagSystem.Retrieval.Services;
using RagSystem.Retrieval.Settings;

namespace RagSystem.Retrieval;

public static class RetrievalServices
{
    /// <summary>Registers the retrieval settings, the search and the post-processing. A wrong value stops the app at start.</summary>
    public static IServiceCollection AddRetrieval(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = (configuration.GetSection(RetrievalSettings.SectionName).Get<RetrievalSettings>() ?? new RetrievalSettings()).EnsureValid();
        services.AddSingleton(settings);
        services.AddSingleton<ResultPostProcessor>();
        services.AddScoped<ChunkSearcher>();
        services.AddScoped<ChunkCitations>();
        return services;
    }
}
