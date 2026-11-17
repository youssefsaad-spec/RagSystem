using Microsoft.EntityFrameworkCore;
using Pgvector;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Documents.Models;
using RagSystem.Documents.Services;
using RagSystem.Ollama.Services;
using RagSystem.Ollama.Settings;

namespace RagSystem.Documents.Workers;

/// <summary>
/// Runs one job: embeds every chunk, then in one transaction replaces the document's old chunks with the new ones, marks
/// the document Ready and bumps the version's revision. If anything fails nothing is stored: the document is marked Failed
/// with the error text, and the old chunks (if any) are untouched. A re-upload starts again from the beginning.
/// </summary>
public sealed class IngestionJobRunner(
    RagDbContext db,
    BatchEmbedder embedder,
    OllamaSettings ollama,
    TimeProvider time,
    ILogger<IngestionJobRunner> logger)
{
    /// <summary>The error text kept on a Failed document is cut to this many characters.</summary>
    public const int MaxErrorLength = 2000;

    public async Task RunAsync(IngestionJob job, CancellationToken ct)
    {
        try
        {
            var vectors = await embedder.EmbedAsync(job.Result.Chunks.Select(c => c.Text).ToList(), ct);
            if (!await StoreAsync(job, vectors, ct))
                logger.LogWarning("Document {Document} ({File}, {Version}) was deleted while it was processed; nothing stored",
                    job.DocumentId, job.FileName, job.Version);
            else
                logger.LogInformation("Stored {File} ({Version}): {Chunks} chunks", job.FileName, job.Version, job.Result.Chunks.Count);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Ingestion of {File} ({Version}) failed", job.FileName, job.Version);
            await MarkFailedAsync(job.DocumentId, error.Message);
        }
    }

    /// <returns>False when the document no longer exists.</returns>
    private async Task<bool> StoreAsync(IngestionJob job, IReadOnlyList<float[]> vectors, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == job.DocumentId, ct);
        if (document is null)
            return false;

        await db.Chunks.Where(c => c.DocumentId == job.DocumentId).ExecuteDeleteAsync(ct);
        db.Chunks.AddRange(job.Result.Chunks.Select((chunk, index) => new Chunk
        {
            Id = Guid.CreateVersion7(),
            DocumentId = job.DocumentId,
            Version = job.Version,
            Ordinal = chunk.Ordinal,
            Content = chunk.Text,
            EstTokens = chunk.EstTokens,
            Embedding = new Vector(vectors[index]),
            EmbeddingModel = ollama.EmbeddingModel,
            CreatedAt = now,
        }));

        document.Title = job.Result.Title;
        document.Status = DocumentStatus.Ready;
        document.ChunkCount = job.Result.Chunks.Count;
        document.Error = null;
        document.ReadyAt = now;
        await db.SaveChangesAsync(ct);

        await db.BumpAsync(job.Version, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task MarkFailedAsync(Guid documentId, string error)
    {
        db.ChangeTracker.Clear();
        var text = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        await db.Documents.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.Status, DocumentStatus.Failed).SetProperty(d => d.Error, text));
    }
}
