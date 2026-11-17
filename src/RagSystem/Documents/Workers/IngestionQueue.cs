using System.Threading.Channels;
using RagSystem.Documents.Models;

namespace RagSystem.Documents.Workers;

/// <summary>The in-memory line between the upload and the background worker. One reader. Jobs are lost when the app stops.</summary>
public sealed class IngestionQueue
{
    private readonly Channel<IngestionJob> _channel = Channel.CreateUnbounded<IngestionJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(IngestionJob job, CancellationToken ct = default) => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<IngestionJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
