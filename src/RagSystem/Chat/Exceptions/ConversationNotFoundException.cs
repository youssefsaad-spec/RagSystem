namespace RagSystem.Chat.Exceptions;

/// <summary>The conversation does not exist, or belongs to another client or version. Answered as <c>404 conversation_not_found</c>, so the two cases look the same.</summary>
public sealed class ConversationNotFoundException(Guid id) : Exception($"Conversation {id} was not found.");
