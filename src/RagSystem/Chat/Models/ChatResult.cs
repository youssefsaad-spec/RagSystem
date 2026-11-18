using RagSystem.Retrieval.Models;

namespace RagSystem.Chat.Models;

/// <summary>The outcome of one chat turn, before it is shaped for the HTTP answer.</summary>
public sealed record ChatResult(
    Guid ConversationId,
    Guid MessageId,
    string Answer,
    IReadOnlyList<RetrievedSource> Citations,
    int InputTokens,
    int OutputTokens,
    int ThinkingTokens,
    bool CacheHit);
