namespace RagSystem.Retrieval.Models;

/// <summary>A candidate that was left out, and why. The pipeline logs these (F37) so the thresholds can be tuned (F28).</summary>
public sealed record DroppedCandidate(ChunkCandidate Candidate, DropReason Reason);
