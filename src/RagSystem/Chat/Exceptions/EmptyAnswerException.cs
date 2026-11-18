namespace RagSystem.Chat.Exceptions;

/// <summary>The model answered, but nothing is left once the sources line is taken off (or it answered nothing at all). Answered as <c>502 empty_answer</c>.</summary>
public sealed class EmptyAnswerException() : Exception("The model returned an empty answer.");
