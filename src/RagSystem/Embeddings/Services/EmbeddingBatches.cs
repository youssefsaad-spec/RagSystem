using RagSystem.Text;

namespace RagSystem.Embeddings.Services;

/// <summary>Decides which consecutive texts are sent to the embedding service together.</summary>
public static class EmbeddingBatches
{
    public const int MaxItems = 32;

    public const int MaxEstimatedTokens = 6000;

    /// <summary>
    /// Splits the texts into consecutive batches. A batch is closed when it has 32 texts or when the next text would take it
    /// over 6,000 estimated tokens. A single text over the limit gets a batch of its own.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> Plan(IReadOnlyList<string> texts)
    {
        var batches = new List<(int Start, int Count)>();
        var start = 0;
        var tokens = 0;
        for (var i = 0; i < texts.Count; i++)
        {
            var next = TokenEstimator.Estimate(texts[i]);
            var count = i - start;
            if (count > 0 && (count >= MaxItems || tokens + next > MaxEstimatedTokens))
            {
                batches.Add((start, count));
                start = i;
                tokens = 0;
            }
            tokens += next;
        }
        if (start < texts.Count)
            batches.Add((start, texts.Count - start));
        return batches;
    }
}
