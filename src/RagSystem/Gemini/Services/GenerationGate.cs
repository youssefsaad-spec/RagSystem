using RagSystem.Chat.Settings;
using RagSystem.Embeddings.Exceptions;

namespace RagSystem.Gemini.Services;

/// <summary>
/// Lets only <c>Chat:MaxConcurrentGenerations</c> model calls run at once (default 1). Answers and query rewrites share it.
/// Time spent waiting for a place counts against the call's timeout. The place is given back after a failure too.
/// </summary>
public sealed class GenerationGate(ChatSettings settings) : IDisposable
{
    private readonly SemaphoreSlim _places = new(settings.MaxConcurrentGenerations, settings.MaxConcurrentGenerations);

    /// <summary>Runs <paramref name="work"/> when a place is free. <paramref name="work"/> gets a token that is cancelled at the timeout.</summary>
    /// <exception cref="EmbeddingException">Transient: there was no free place in time, or the work did not finish in time.</exception>
    /// <exception cref="OperationCanceledException">Only when the caller cancelled.</exception>
    public async Task<T> RunAsync<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        try
        {
            await _places.WaitAsync(limit.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingException(EmbeddingFailureKind.Transient, "The model is busy: no free place for this request in time.", inner: error);
        }

        try
        {
            return await work(limit.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingException(EmbeddingFailureKind.Transient, "The model did not answer in time.", inner: error);
        }
        finally
        {
            _places.Release();
        }
    }

    public void Dispose() => _places.Dispose();
}
