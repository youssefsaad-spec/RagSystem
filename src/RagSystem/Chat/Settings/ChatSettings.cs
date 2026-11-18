namespace RagSystem.Chat.Settings;

/// <summary>
/// How a chat turn is run. The values live in the "Chat" section of appsettings.json; the defaults here are the same values and
/// are used when a value is missing. A wrong value stops the app at start.
/// </summary>
public sealed record ChatSettings
{
    public const string SectionName = "Chat";

    /// <summary>How many model calls (answers and rewrites together) may run at the same time (F21).</summary>
    public int MaxConcurrentGenerations { get; init; } = 1;

    /// <summary>Longest an answer may take, including the wait for a free place at the gate. Over it: <c>503 llm_unavailable</c>.</summary>
    public int ChatTimeoutSeconds { get; init; } = 60;

    /// <summary>Longest a query rewrite may take, including the wait for the gate. Over it, the fallback query is used (F27).</summary>
    public int RewriteTimeoutSeconds { get; init; } = 10;

    /// <summary>Most earlier question-and-answer pairs that go into the prompt (F26).</summary>
    public int HistoryMaxTurns { get; init; } = 6;

    /// <summary>Most estimated tokens of earlier pairs that go into the prompt (F26).</summary>
    public int HistoryMaxTokens { get; init; } = 2000;

    /// <summary>How many of the latest pairs the query rewrite reads (F27).</summary>
    public int RewriteHistoryTurns { get; init; } = 3;

    /// <summary>Most tokens the rewrite may write (F27).</summary>
    public int RewriteMaxOutputTokens { get; init; } = 300;

    /// <summary>Returns this object, or throws when a value cannot work.</summary>
    public ChatSettings EnsureValid()
    {
        Positive(MaxConcurrentGenerations, nameof(MaxConcurrentGenerations));
        Positive(ChatTimeoutSeconds, nameof(ChatTimeoutSeconds));
        Positive(RewriteTimeoutSeconds, nameof(RewriteTimeoutSeconds));
        Positive(HistoryMaxTurns, nameof(HistoryMaxTurns));
        Positive(HistoryMaxTokens, nameof(HistoryMaxTokens));
        Positive(RewriteHistoryTurns, nameof(RewriteHistoryTurns));
        Positive(RewriteMaxOutputTokens, nameof(RewriteMaxOutputTokens));
        return this;
    }

    private static void Positive(int value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"{SectionName}:{name} must be above 0, but it is {value}.");
    }
}
