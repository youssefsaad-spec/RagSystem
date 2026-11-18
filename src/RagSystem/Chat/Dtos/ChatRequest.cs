namespace RagSystem.Chat.Dtos;

/// <summary>The body of <c>POST /api/{version}/chat</c>.</summary>
/// <param name="ConversationId">The conversation to continue. Null or missing starts a new one.</param>
/// <param name="Message">The question.</param>
public sealed record ChatRequest(Guid? ConversationId, string? Message);

/// <summary>What <c>ChatRequestValidator</c> checks: the version from the route and the message from the body.</summary>
public sealed record ChatCall(string? Version, string? Message);
