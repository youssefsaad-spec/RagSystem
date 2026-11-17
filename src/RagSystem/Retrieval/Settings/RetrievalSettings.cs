namespace RagSystem.Retrieval.Settings;

/// <summary>
/// How a question finds its documentation. The values live in the "Retrieval" section of appsettings.json; the defaults
/// here are the same values and are used when a value is missing. MinScore and DuplicateSimilarity were measured on
/// another embedding model and are meant to be re-tuned (F28).
/// </summary>
public sealed record RetrievalSettings
{
    public const string SectionName = "Retrieval";

    /// <summary>Most candidates the vector search returns (the "top K"). Between 1 and <see cref="MaxTopK"/>.</summary>
    public int TopK { get; init; } = 20;

    /// <summary>A candidate with a cosine score below this is dropped. 0 keeps everything.</summary>
    public double MinScore { get; init; } = 0.60;

    /// <summary>
    /// A candidate whose cosine similarity to an already chosen chunk is above this is dropped as a near-duplicate
    /// (Chapter 9 and whats-new-v3.md repeat text). 1 drops only exact copies.
    /// </summary>
    public double DuplicateSimilarity { get; init; } = 0.95;

    /// <summary>Most estimated tokens of documentation sent to the model (the first source is kept even if it is larger).</summary>
    public int ContextBudgetTokens { get; init; } = 5000;

    public const int MaxTopK = 200;

    /// <summary>Returns this object, or throws when a value cannot work.</summary>
    public RetrievalSettings EnsureValid()
    {
        if (TopK is < 1 or > MaxTopK)
            throw new InvalidOperationException($"{SectionName}:TopK must be from 1 to {MaxTopK}, but it is {TopK}.");
        if (MinScore is < 0 or > 1 || double.IsNaN(MinScore))
            throw new InvalidOperationException($"{SectionName}:MinScore must be from 0 to 1, but it is {MinScore}.");
        if (DuplicateSimilarity is < 0 or > 1 || double.IsNaN(DuplicateSimilarity))
            throw new InvalidOperationException($"{SectionName}:DuplicateSimilarity must be from 0 to 1, but it is {DuplicateSimilarity}.");
        if (ContextBudgetTokens < 1)
            throw new InvalidOperationException($"{SectionName}:ContextBudgetTokens must be above 0, but it is {ContextBudgetTokens}.");
        return this;
    }
}
