namespace RagSystem.Chat.Rules;

/// <summary>Why a query rewrite fell back to "previous question + current question" (F27). Saved in the user message's guard flags.</summary>
public static class RewriteReasons
{
    public const string Timeout = "timeout";
    public const string RateLimited = "rate_limited";
    public const string Error = "error";
    public const string InvalidOutput = "invalid_output";
    // client_budget, project_limit and project_daily_limit come with F31 and F32.
}
