namespace RagSystem.Retrieval.Rules;

public static class QuestionLimits
{
    /// <summary>A question longer than this (estimated tokens) is refused: <c>400 question_too_long</c>.</summary>
    public const int MaxTokens = 1000;
}
