using Microsoft.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Documents.Dtos;

namespace RagSystem.Documents.Services;

/// <summary>Reads documents and their chunks. The version is always part of the question: a document of another version is not found.</summary>
public sealed class DocumentQueries(RagDbContext db)
{
    public const int DefaultTake = 100;

    public const int MaxTake = 500;

    public async Task<IReadOnlyList<DocumentView>> ListAsync(string version, CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.Version == version)
            .OrderBy(d => d.FileName)
            .Select(d => ToView(d))
            .ToListAsync(ct);

    public async Task<DocumentView?> GetAsync(string version, Guid id, CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.Version == version && d.Id == id)
            .Select(d => ToView(d))
            .FirstOrDefaultAsync(ct);

    /// <summary>The stored chunks in document order, or null when the document does not exist.</summary>
    /// <param name="skip">Negative counts as 0.</param>
    /// <param name="take">Default 100; at least 1 and at most 500.</param>
    public async Task<IReadOnlyList<ChunkView>?> ChunksAsync(string version, Guid id, int? skip, int? take, CancellationToken ct)
    {
        if (!await db.Documents.AnyAsync(d => d.Version == version && d.Id == id, ct))
            return null;

        return await db.Chunks.AsNoTracking()
            .Where(c => c.DocumentId == id)
            .OrderBy(c => c.Ordinal)
            .Skip(Math.Max(skip ?? 0, 0))
            .Take(Math.Clamp(take ?? DefaultTake, 1, MaxTake))
            .Select(c => new ChunkView(c.Id, c.Ordinal, c.Content, c.EstTokens, c.EmbeddingModel))
            .ToListAsync(ct);
    }

    private static DocumentView ToView(Document d) =>
        new(d.Id, d.Version, d.FileName, d.Title, d.Status.ToString(), d.ChunkCount, d.Error, d.UploadedAt, d.ReadyAt);
}
