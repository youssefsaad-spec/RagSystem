namespace RagSystem.Documents.Dtos;

/// <summary>The answer to an accepted upload (202).</summary>
public sealed record AcceptedDocument(Guid Id, string Status);
