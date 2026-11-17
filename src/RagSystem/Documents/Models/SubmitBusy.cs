namespace RagSystem.Documents.Models;

/// <summary>The same file (same version and name) is still being processed.</summary>
public sealed record SubmitBusy(Guid Id) : SubmitOutcome;
