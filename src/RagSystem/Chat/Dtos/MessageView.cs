namespace RagSystem.Chat.Dtos;

/// <summary>One stored message, as <c>GET /api/{version}/conversations/{id}/messages</c> shows it.</summary>
/// <param name="Role">"user" or "assistant".</param>
/// <param name="Citations">On an answer: its sources. Empty on a question.</param>
/// <param name="Usage">On an answer: the token counts. Null on a question.</param>
public sealed record MessageView(
    Guid Id,
    string Role,
    string Content,
    IReadOnlyList<ChatCitation> Citations,
    ChatUsage? Usage,
    DateTimeOffset CreatedAt);
