using Microsoft.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Retrieval.Models;

namespace RagSystem.Retrieval.Services;

/// <summary>
/// Finds the document title and position of chunks by id. Used when an answer is rebuilt from stored chunk ids
/// (a cache hit, F35) and not from a fresh search.
/// </summary>
public sealed class ChunkCitations(RagDbContext db)
{
    /// <summary>
    /// The citations of these chunks in the version, in the order of <paramref name="chunkIds"/>. An id that is not found
    /// (the document was deleted, or the id belongs to another version) is left out.
    /// </summary>
    public async Task<IReadOnlyList<ChunkCitation>> FindAsync(string version, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        if (chunkIds.Count == 0)
            return [];

        var found = await (
                from chunk in db.Chunks.AsNoTracking()
                where chunk.Version == version && chunkIds.Contains(chunk.Id)
                join document in db.Documents on chunk.DocumentId equals document.Id
                select new ChunkCitation(chunk.Id, document.Title, chunk.Ordinal))
            .ToDictionaryAsync(c => c.ChunkId, ct);

        return chunkIds.Distinct().Where(found.ContainsKey).Select(id => found[id]).ToList();
    }
}
