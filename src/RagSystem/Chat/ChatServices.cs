using Microsoft.Extensions.AI;
using RagSystem.Chat.Services;
using RagSystem.Chat.Settings;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;

namespace RagSystem.Chat;

public static class ChatServices
{
    /// <summary>
    /// Registers the chat settings, the generation gate, the chat model and the steps of a chat turn. A wrong setting stops the app
    /// at start. Needs <c>AddGemini</c>, <c>AddEmbeddings</c>, <c>AddRetrieval</c> and <c>AddDatabase</c>.
    /// </summary>
    public static IServiceCollection AddChat(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = (configuration.GetSection(ChatSettings.SectionName).Get<ChatSettings>() ?? new ChatSettings()).EnsureValid();
        services.AddSingleton(settings);

        // One gate for the whole app: it is the real capacity limit, shared by answers and rewrites.
        services.AddSingleton<GenerationGate>();
        services.AddSingleton(provider => new GeminiChat(
            () => provider.GetRequiredService<IChatClient>(), // asked for on first use: without an API key only a chat call fails
            provider.GetRequiredService<GeminiSettings>(),
            provider.GetRequiredService<GenerationGate>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<GeminiChat>()));

        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<QueryRewriter>();
        services.AddScoped<ConversationStore>();
        services.AddScoped<ChatPipeline>();
        return services;
    }
}
