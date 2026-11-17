namespace RagSystem.Retrieval.Models;

/// <summary>Why a candidate did not reach the documentation sent to the model.</summary>
public enum DropReason
{
    /// <summary>Its score is under <c>Retrieval:MinScore</c>.</summary>
    BelowMinScore,

    /// <summary>It is too similar to a chunk that was already chosen.</summary>
    NearDuplicate,

    /// <summary>The <c>Retrieval:ContextBudgetTokens</c> budget has no room for it.</summary>
    OverBudget,
}
