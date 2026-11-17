using Microsoft.Extensions.AI;
using RagSystem.Embeddings.Exceptions;

namespace RagSystem.Embeddings.Services;

/// <summary>
/// Embeds many texts: splits them into batches (<see cref="EmbeddingBatches"/>), sends each batch to the embedding generator (Gemini or Ollama, through the Microsoft.Extensions.AI interface), retries only
/// the batch that failed, and returns unit-length vectors. Used by ingestion for the chunk texts, which are embedded as they are.
/// </summary>
/// <param name="delay">Waits between attempts. Only tests pass their own, so they do not really wait.</param>
public sealed class BatchEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    ILogger<BatchEmbedder> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>Tries per batch, including the first.</summary>
    public const int MaxAttempts = 7;

    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(60);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));

    /// <summary>One vector per text, in the same order, each of length 1.</summary>
    /// <exception cref="EmbeddingException">A batch failed for good: a fatal error, or it still failed after <see cref="MaxAttempts"/> tries.</exception>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var batches = EmbeddingBatches.Plan(texts);
        var vectors = new List<float[]>(texts.Count);

        for (var number = 1; number <= batches.Count; number++)
        {
            var (start, count) = batches[number - 1];
            var batch = texts.Skip(start).Take(count).ToList();

            var embedded = await EmbedBatchAsync(batch, number, batches.Count, ct);

            vectors.AddRange(embedded.Select(UnitVector.Of));
            logger.LogInformation("Embedded {Done}/{Total} texts (batch {Batch}/{Batches})", vectors.Count, texts.Count, number, batches.Count);
        }
        return vectors;
    }

    private async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> batch, int number, int batches, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var embeddings = await generator.GenerateAsync(batch, cancellationToken: ct);
                return [.. embeddings.Select(e => e.Vector.ToArray())];
            }
            catch (EmbeddingException error) when (error.Kind != EmbeddingFailureKind.Fatal)
            {
                if (attempt >= MaxAttempts)
                    throw new EmbeddingException(error.Kind,
                        $"Gave up on batch {number}/{batches} after {attempt} attempts: {error.Message}", error.RetryAfter, error);

                var wait = error.RetryAfter ?? Backoff(attempt);
                logger.LogWarning("Embedding batch {Batch}/{Batches} failed (attempt {Attempt}, {Kind}); retrying in {Wait:F0}s: {Message}",
                    number, batches, attempt, error.Kind, wait.TotalSeconds, error.Message);
                await _delay(wait, ct);
            }
        }
    }

    /// <summary>2 s, then doubling: 2, 4, 8, 16, 32, then 60 s at most.</summary>
    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(FirstWait.TotalSeconds * Math.Pow(2, attempt - 1), LongestWait.TotalSeconds));
}
