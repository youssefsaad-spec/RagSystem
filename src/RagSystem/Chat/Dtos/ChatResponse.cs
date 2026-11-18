using RagSystem.Chat.Models;
using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Rules;

namespace RagSystem.Chat.Dtos;

/// <summary>The answer of one chat turn.</summary>
public sealed record ChatResponse(
    Guid ConversationId,
    Guid MessageId,
    string Answer,
    IReadOnlyList<ChatCitation> Citations,
    ChatUsage Usage)
{
    public static ChatResponse From(ChatResult result) => new(
        result.ConversationId,
        result.MessageId,
        result.Answer,
        result.Citations.Select(s => ChatCitation.Of(s.DocumentTitle, s.Ordinal)).ToList(),
        new ChatUsage(result.InputTokens, result.OutputTokens, result.ThinkingTokens, result.CacheHit));
}

/// <summary>A source of an answer: the document title and the part number in it (the first part is 1).</summary>
/// <param name="Text">For example "NDC Portal APIs V3, part 12".</param>
public sealed record ChatCitation(string Title, int Part, string Text)
{
    /// <param name="ordinal">The stored position, starting at 0.</param>
    public static ChatCitation Of(string title, int ordinal) => new(title, ordinal + 1, CitationText.Of(title, ordinal));
}

/// <summary>Tokens the model service reported for the answer. All 0 when no model was called.</summary>
public sealed record ChatUsage(int InputTokens, int OutputTokens, int ThinkingTokens, bool CacheHit);
