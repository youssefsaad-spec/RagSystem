namespace RagSystem.Gemini.Models;

/// <summary>What the chat model answered, as the service reported it.</summary>
/// <param name="Truncated">The model stopped because it reached the output limit, so the text is cut off.</param>
/// <param name="Model">The model that answered.</param>
public sealed record ModelReply(string Text, int InputTokens, int OutputTokens, int ThinkingTokens, bool Truncated, string Model);
