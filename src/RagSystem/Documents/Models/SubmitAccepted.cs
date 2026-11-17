namespace RagSystem.Documents.Models;

/// <summary>The file is valid and queued. The document is Processing until the worker has stored it.</summary>
public sealed record SubmitAccepted(Guid Id) : SubmitOutcome;
