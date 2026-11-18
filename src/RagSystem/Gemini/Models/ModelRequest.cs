using Microsoft.Extensions.AI;

namespace RagSystem.Gemini.Models;

/// <summary>One call to the chat model.</summary>
/// <param name="Messages">The system message first, then the conversation, the last message from the user.</param>
/// <param name="Model">The Gemini model to use for this call (the answer model or the rewrite model).</param>
/// <param name="Timeout">Limit for the whole call, including the wait for the gate (F21).</param>
public sealed record ModelRequest(IReadOnlyList<ChatMessage> Messages, string Model, int MaxOutputTokens, TimeSpan Timeout);
