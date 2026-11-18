namespace RagSystem.Chat.Models;

/// <summary>One earlier question and its answer, as the model sees them in the conversation.</summary>
public sealed record HistoryTurn(string Question, string Answer, int EstTokens);
