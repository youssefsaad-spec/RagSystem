using Microsoft.Extensions.AI;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Observability;

namespace RagSystem.Embeddings.Services;

/// <summary>
/// Embeds one question for the search. The question is sent exactly as the user wrote it, the same way the chunks were
/// embedded, so question and chunks are compared in the same way. A single call, no retry: a failure goes to the caller,
/// who answers 503 or 502 (F24).
/// </summary>
public sealed class QueryEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator)
{
    /// <summary>The vector of the question, length 1, with the length checked.</summary>
    /// <exception cref="EmbeddingException">The call failed (see <see cref="EmbeddingException.Kind"/>) or the answer is unusable.</exception>
    public Task<float[]> EmbedAsync(string question, CancellationToken ct = default) =>
        Telemetry.TraceAsync("embed_query", Telemetry.Kinds.Embedding, async span =>
        {
            span.SetInput(question);
            var embeddings = await generator.GenerateAsync([question], cancellationToken: ct);
            var vector = UnitVector.Of(embeddings[0].Vector.ToArray());
            span.SetMeta("dimensions", vector.Length); // the vector itself is not sent
            return vector;
        });
}
