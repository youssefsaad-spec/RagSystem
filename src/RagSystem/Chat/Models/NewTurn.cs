namespace RagSystem.Chat.Models;

/// <summary>One finished exchange, ready to be saved (F26).</summary>
/// <param name="UserFlags">Notes about the question, for example <c>rewrite</c> = the fallback reason. Null or empty when there are none.</param>
/// <param name="Model">The model that was asked. Null when no model was called (a "not covered" answer).</param>
/// <param name="ModelVersion">The model name the service reported.</param>
/// <param name="AssistantFlags">Notes about the answer, for example <c>sources_missing</c> or <c>truncated</c>.</param>
public sealed record NewTurn(
    string Question,
    string RetrievalQuery,
    IReadOnlyDictionary<string, string>? UserFlags,
    string Answer,
    string? Model,
    string? ModelVersion,
    int InputTokens,
    int OutputTokens,
    int ThinkingTokens,
    IReadOnlyList<Guid> CitedChunkIds,
    IReadOnlyDictionary<string, string>? AssistantFlags);
