using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using RagSystem.Gemini;
using RagSystem.Gemini.Settings;

namespace RagSystem.Tests.Gemini;

public class GeminiServicesTests
{
    private static ServiceProvider ProviderWith(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddGemini(configuration).BuildServiceProvider();
    }

    [Fact]
    public void With_a_key_the_app_gets_a_chat_client_through_the_neutral_interface()
    {
        using var provider = ProviderWith(new() { ["Gemini:ApiKey"] = "test-key" });

        Assert.NotNull(provider.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void Without_a_key_the_app_still_starts_and_only_the_chat_client_refuses_with_a_clear_message()
    {
        using var provider = ProviderWith([]);

        Assert.NotNull(provider.GetRequiredService<GeminiSettings>()); // startup is fine
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IChatClient>());
        Assert.Contains("Gemini:ApiKey", error.Message);
    }

    [Fact]
    public void A_wrong_setting_stops_the_app_at_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProviderWith(new() { ["Gemini:Temperature"] = "5" }));

        Assert.Contains("Gemini:Temperature", error.Message);
    }
}
