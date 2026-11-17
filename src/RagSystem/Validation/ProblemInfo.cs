namespace RagSystem.Validation;

/// <summary>How a failed rule is answered: the HTTP status and the title. The rule's error code is the <c>reason</c>, its message the <c>detail</c>.</summary>
public sealed record ProblemInfo(int Status, string Title);
