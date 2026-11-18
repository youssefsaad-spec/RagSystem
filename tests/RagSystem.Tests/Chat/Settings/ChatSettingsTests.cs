using RagSystem.Chat.Settings;

namespace RagSystem.Tests.Chat.Settings;

public class ChatSettingsTests
{
    [Fact]
    public void The_defaults_are_valid()
    {
        var settings = new ChatSettings().EnsureValid();

        Assert.Equal(1, settings.MaxConcurrentGenerations);
        Assert.Equal(6, settings.HistoryMaxTurns);
        Assert.Equal(2000, settings.HistoryMaxTokens);
        Assert.Equal(3, settings.RewriteHistoryTurns);
        Assert.Equal(10, settings.RewriteTimeoutSeconds);
        Assert.Equal(300, settings.RewriteMaxOutputTokens);
    }

    [Theory]
    [InlineData(nameof(ChatSettings.MaxConcurrentGenerations))]
    [InlineData(nameof(ChatSettings.ChatTimeoutSeconds))]
    [InlineData(nameof(ChatSettings.RewriteTimeoutSeconds))]
    [InlineData(nameof(ChatSettings.HistoryMaxTurns))]
    [InlineData(nameof(ChatSettings.HistoryMaxTokens))]
    [InlineData(nameof(ChatSettings.RewriteHistoryTurns))]
    [InlineData(nameof(ChatSettings.RewriteMaxOutputTokens))]
    public void A_value_of_zero_or_less_stops_the_app_and_the_message_names_the_setting(string name)
    {
        var settings = name switch
        {
            nameof(ChatSettings.MaxConcurrentGenerations) => new ChatSettings { MaxConcurrentGenerations = 0 },
            nameof(ChatSettings.ChatTimeoutSeconds) => new ChatSettings { ChatTimeoutSeconds = 0 },
            nameof(ChatSettings.RewriteTimeoutSeconds) => new ChatSettings { RewriteTimeoutSeconds = -1 },
            nameof(ChatSettings.HistoryMaxTurns) => new ChatSettings { HistoryMaxTurns = 0 },
            nameof(ChatSettings.HistoryMaxTokens) => new ChatSettings { HistoryMaxTokens = 0 },
            nameof(ChatSettings.RewriteHistoryTurns) => new ChatSettings { RewriteHistoryTurns = 0 },
            _ => new ChatSettings { RewriteMaxOutputTokens = 0 },
        };

        var error = Assert.Throws<InvalidOperationException>(() => settings.EnsureValid());

        Assert.Contains($"Chat:{name}", error.Message);
    }
}
