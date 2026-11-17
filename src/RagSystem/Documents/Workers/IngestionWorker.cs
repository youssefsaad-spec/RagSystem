using Microsoft.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Database.Entities;

namespace RagSystem.Documents.Workers;

/// <summary>Background service: takes jobs off the queue one at a time and runs them.</summary>
public sealed class IngestionWorker(IngestionQueue queue, IServiceScopeFactory scopes, ILogger<IngestionWorker> logger) : BackgroundService
{
    public const string InterruptedMessage = "Interrupted by a restart before processing finished. Upload the file again.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await FailInterruptedDocumentsAsync(stoppingToken);

        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IngestionJobRunner>().RunAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down: the document stays Processing and is marked Failed at the next start
            }
            catch (Exception error)
            {
                logger.LogError(error, "Unexpected error while processing {File}", job.FileName);
            }
        }
    }

    /// <summary>The queue is in memory, so a restart loses waiting jobs. Say so, instead of leaving documents "Processing" for ever.</summary>
    private async Task FailInterruptedDocumentsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var count = await db.Documents.Where(d => d.Status == DocumentStatus.Processing)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.Status, DocumentStatus.Failed).SetProperty(d => d.Error, InterruptedMessage), ct);
            if (count > 0)
                logger.LogWarning("Marked {Count} interrupted document(s) as Failed", count);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The database may be unreachable or not migrated yet; the worker still runs.
            logger.LogWarning(error, "Could not check for interrupted documents");
        }
    }
}
