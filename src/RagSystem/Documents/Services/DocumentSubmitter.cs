using Microsoft.EntityFrameworkCore;
using Npgsql;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Documents.Models;
using RagSystem.Documents.Workers;
using RagSystem.Ingestion.Services;

namespace RagSystem.Documents.Services;

/// <summary>
/// Takes an uploaded file: reads and cuts it, and, unless it is a dry run or not valid, creates or reuses the document
/// row as Processing and queues the job. The same version and file name is the same document, so a re-upload replaces it
/// (its old chunks stay until the new ones are stored).
/// </summary>
public sealed class DocumentSubmitter(
    DocumentProcessor processor,
    RagDbContext db,
    IngestionQueue queue,
    TimeProvider time,
    ILogger<DocumentSubmitter> logger)
{
    /// <exception cref="UnsupportedFileTypeException">The file is not .md or .json.</exception>
    public async Task<SubmitOutcome> SubmitAsync(string version, string fileName, byte[] content, bool dryRun, CancellationToken ct)
    {
        var result = processor.Process(fileName, content, version);
        logger.LogInformation("Read {File} for {Version}: {Chunks} chunk(s), {Errors} error(s), {Warnings} warning(s), dry run {DryRun}",
            fileName, version, result.Chunks.Count, result.Errors.Count(), result.Warnings.Count(), dryRun);
        if (dryRun || !result.IsValid)
            return new SubmitReport(result, dryRun);

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Version == version && d.FileName == fileName, ct);
        if (document is { Status: DocumentStatus.Processing })
            return Busy(document.Id, fileName, version);

        if (document is null)
        {
            document = new Document { Id = Guid.CreateVersion7(), Version = version, FileName = fileName };
            db.Documents.Add(document);
        }
        document.Title = result.Title;
        document.Status = DocumentStatus.Processing;
        document.Error = null;
        document.UploadedAt = time.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another upload of the same file created the row a moment ago.
            db.ChangeTracker.Clear();
            var other = await db.Documents.FirstAsync(d => d.Version == version && d.FileName == fileName, ct);
            return Busy(other.Id, fileName, version);
        }

        await queue.EnqueueAsync(new IngestionJob(document.Id, version, fileName, result), ct);
        logger.LogInformation("Queued {File} for {Version} as document {Document}", fileName, version, document.Id);
        return new SubmitAccepted(document.Id);
    }

    private SubmitBusy Busy(Guid id, string fileName, string version)
    {
        logger.LogWarning("{File} for {Version} is still being processed (document {Document}); upload refused", fileName, version, id);
        return new SubmitBusy(id);
    }
}
