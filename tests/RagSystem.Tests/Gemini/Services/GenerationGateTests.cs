using RagSystem.Chat.Settings;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Gemini.Services;

namespace RagSystem.Tests.Gemini.Services;

public class GenerationGateTests
{
    private static GenerationGate Gate(int places = 1) => new(new ChatSettings { MaxConcurrentGenerations = places });

    [Fact]
    public async Task Work_runs_and_its_result_is_returned()
    {
        using var gate = Gate();

        var result = await gate.RunAsync(TimeSpan.FromSeconds(5), _ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Only_one_call_runs_at_a_time_and_the_next_one_starts_when_the_first_ends()
    {
        using var gate = Gate();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var secondStarted = false;

        var first = gate.RunAsync(TimeSpan.FromSeconds(5), async _ => { firstStarted.SetResult(); await release.Task; return 1; }, CancellationToken.None);
        await firstStarted.Task;
        var second = gate.RunAsync(TimeSpan.FromSeconds(5), _ => { secondStarted = true; return Task.FromResult(2); }, CancellationToken.None);

        await Task.Delay(100);
        Assert.False(secondStarted); // still waiting for the place

        release.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task Waiting_for_a_place_counts_against_the_timeout_and_is_a_transient_failure()
    {
        using var gate = Gate();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var first = gate.RunAsync(TimeSpan.FromSeconds(5), async _ => { firstStarted.SetResult(); await release.Task; return 1; }, CancellationToken.None);
        await firstStarted.Task;

        var error = await Assert.ThrowsAsync<EmbeddingException>(
            () => gate.RunAsync(TimeSpan.FromMilliseconds(100), _ => Task.FromResult(2), CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        release.SetResult();
        await first;
    }

    [Fact]
    public async Task Work_that_takes_too_long_is_a_transient_failure_and_the_place_is_given_back()
    {
        using var gate = Gate();

        var error = await Assert.ThrowsAsync<EmbeddingException>(
            () => gate.RunAsync<int>(TimeSpan.FromMilliseconds(100), async token => { await Task.Delay(Timeout.Infinite, token); return 0; }, CancellationToken.None));
        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);

        // The place is free again.
        Assert.Equal(7, await gate.RunAsync(TimeSpan.FromSeconds(5), _ => Task.FromResult(7), CancellationToken.None));
    }

    [Fact]
    public async Task A_failure_of_the_work_gives_the_place_back_and_reaches_the_caller()
    {
        using var gate = Gate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.RunAsync<int>(TimeSpan.FromSeconds(5), _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(7, await gate.RunAsync(TimeSpan.FromSeconds(5), _ => Task.FromResult(7), CancellationToken.None));
    }

    [Fact]
    public async Task When_the_caller_cancels_it_is_a_cancellation_not_a_model_failure()
    {
        using var gate = Gate();
        using var cancel = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.RunAsync<int>(TimeSpan.FromSeconds(30), async token => { await Task.Delay(Timeout.Infinite, token); return 0; }, cancel.Token));
    }

    [Fact]
    public async Task More_places_let_more_calls_run_together()
    {
        using var gate = Gate(places: 2);
        var release = new TaskCompletionSource();
        var started = 0;

        Task<int> Call() => gate.RunAsync(TimeSpan.FromSeconds(5), async _ => { Interlocked.Increment(ref started); await release.Task; return 1; }, CancellationToken.None);
        var calls = new[] { Call(), Call() };
        await Task.Delay(100);

        Assert.Equal(2, started);
        release.SetResult();
        await Task.WhenAll(calls);
    }
}
