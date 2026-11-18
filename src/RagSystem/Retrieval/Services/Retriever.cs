using RagSystem.Retrieval.Models;
using RagSystem.Embeddings.Services;
using RagSystem.Observability;

namespace RagSystem.Retrieval.Services;

/// <summary>
/// The whole retrieval for one question: embed it (F17), search the version (F18), post-process the candidates (F19).
/// The search endpoint uses it now; the chat endpoint (F25) will use the same steps.
/// </summary>
public sealed class Retriever(QueryEmbedder embedder, ChunkSearcher searcher, ResultPostProcessor postProcessor)
{
    /// <exception cref="RagSystem.Embeddings.Exceptions.EmbeddingException">The question could not be embedded.</exception>
    public Task<RetrievalResult> RetrieveAsync(string version, string question, CancellationToken ct) =>
        Telemetry.TraceAsync("retrieve", Telemetry.Kinds.Retriever, async span =>
        {
            span.SetInput(question);
            span.SetMeta("version", version);

            var vector = await embedder.EmbedAsync(question, ct);
            var candidates = await searcher.SearchAsync(version, vector, ct);
            var result = postProcessor.Process(candidates);

            // What LangSmith shows as this retriever's output: the chunks the model will read, in prompt order.
            span.SetDocuments(result.Sources.Select(s => (s.Content, (object)new
            {
                label = s.Label,
                chunk_id = s.ChunkId,
                document = s.DocumentTitle,
                ordinal = s.Ordinal,
                score = Math.Round(s.Score, 4),
                est_tokens = s.EstTokens,
            })));
            span.SetMeta("candidates", candidates.Count);
            span.SetMeta("kept", result.Sources.Count);
            span.SetMeta("dropped", result.Dropped.Count);
            return result;
        });
}
