using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Observability;
using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Settings;

namespace RagSystem.Retrieval.Services;

/// <summary>
/// Finds the chunks of one version that are closest to a question, by cosine distance (pgvector <c>&lt;=&gt;</c>).
/// Dense search only, an exact scan with no vector index: a version has a few hundred chunks.
/// </summary>
public sealed class ChunkSearcher(RagDbContext db, RetrievalSettings settings)
{
    /// <summary>
    /// At most <c>Retrieval:TopK</c> candidates, the best match first. The version is a hard filter: a chunk of another
    /// version is never returned.
    /// </summary>
    /// <param name="question">The embedding of the question (see <c>QueryEmbedder</c>).</param>
    public Task<IReadOnlyList<ChunkCandidate>> SearchAsync(string version, float[] question, CancellationToken ct) =>
        Telemetry.TraceAsync("vector_search", Telemetry.Kinds.Retriever, async span =>
        {
            span.SetInputJson(new { version, top_k = settings.TopK });
            var candidates = await SearchCoreAsync(version, question, ct);

            // Every candidate with its score, best first (a short preview of the text: the kept chunks are sent in full by "retrieve").
            span.SetDocuments(candidates.Select(c => (c.Content, Telemetry.DescribeChunk(c))), previewChars: 300);
            span.SetMeta("found", candidates.Count);
            span.SetMeta("best_score", candidates.Count > 0 ? Math.Round(candidates[0].Score, 4) : null);
            span.SetMeta("lowest_score", candidates.Count > 0 ? Math.Round(candidates[^1].Score, 4) : null);
            return candidates;
        });

    private async Task<IReadOnlyList<ChunkCandidate>> SearchCoreAsync(string version, float[] question, CancellationToken ct)
    {
        var vector = new Vector(question);

        var rows = await (
                from chunk in db.Chunks.AsNoTracking()
                where chunk.Version == version
                join document in db.Documents on chunk.DocumentId equals document.Id
                orderby chunk.Embedding.CosineDistance(vector)
                select new
                {
                    chunk.Id,
                    chunk.DocumentId,
                    document.Title,
                    chunk.Ordinal,
                    chunk.Content,
                    chunk.EstTokens,
                    chunk.Embedding,
                    Distance = chunk.Embedding.CosineDistance(vector),
                })
            .Take(settings.TopK)
            .ToListAsync(ct);

        return rows
            .Select(r => new ChunkCandidate(r.Id, r.DocumentId, r.Title, r.Ordinal, r.Content, r.EstTokens, r.Embedding.ToArray(), 1 - r.Distance))
            .ToList();
    }
}
