using System.Diagnostics;
using Microsoft.Extensions.AI;
using RagSystem.Chat.Models;
using RagSystem.Chat.Services;
using RagSystem.Chat.Settings;
using RagSystem.Gemini.Models;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;
using RagSystem.Observability;
using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Services;
using RagSystem.Retrieval.Settings;
using RagSystem.Tests.Gemini;

namespace RagSystem.Tests.Observability;

/// <summary>
/// The spans the pipeline steps produce. They share one static <see cref="Telemetry.Source"/> and one switch, so these tests do
/// not run beside each other (the collection), and each looks only at the spans of its own trace.
/// </summary>
[Collection("Telemetry")]
public sealed class TelemetrySpanTests : IDisposable
{
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;

    public TelemetrySpanTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_spans)
                    _spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        Telemetry.CaptureContent = true;
        _listener.Dispose();
    }

    private Activity Span(string name, Activity root)
    {
        lock (_spans)
            return Assert.Single(_spans, s => s.OperationName == name && s.RootId == root.RootId);
    }

    private static object? Tag(Activity span, string key) => span.GetTagItem(key);

    private static ChunkCandidate Candidate(double score, int axis, int ordinal = 0)
    {
        var vector = new float[8];
        vector[axis] = 1f;
        return new ChunkCandidate(Guid.NewGuid(), Guid.NewGuid(), "Guide", ordinal, $"secret text {ordinal}", 100, vector, score);
    }

    [Fact]
    public void Post_processing_reports_what_it_kept_and_dropped_and_why_without_any_chunk_text()
    {
        using var root = Telemetry.StartRoot("test", Telemetry.Kinds.Chain)!;
        var weak = Candidate(0.2, axis: 0);
        var good = Candidate(0.9, axis: 1);

        new ResultPostProcessor(new RetrievalSettings()).Process([good, weak]);

        var span = Span("post_process", root);
        Assert.Equal("chain", Tag(span, "langsmith.span.kind"));
        Assert.Equal(1, Tag(span, "langsmith.metadata.kept"));
        Assert.Equal(1, Tag(span, "langsmith.metadata.dropped_below_min_score"));
        Assert.Equal(0, Tag(span, "langsmith.metadata.dropped_near_duplicate"));
        var output = (string)Tag(span, "output.value")!;
        Assert.Contains("BelowMinScore", output);
        Assert.Contains(weak.ChunkId.ToString(), output);
        Assert.DoesNotContain("secret text", output);
    }

    [Fact]
    public async Task The_model_call_is_an_llm_span_with_the_messages_the_answer_and_the_token_counts()
    {
        using var root = Telemetry.StartRoot("test", Telemetry.Kinds.Chain)!;
        var chat = new GeminiChat(() => FakeChatClient.Saying("the answer", input: 11, output: 7, thinking: 3), new GeminiSettings(), new GenerationGate(new ChatSettings()));
        var request = new ModelRequest([new(ChatRole.System, "rules"), new(ChatRole.User, "question")], "gemini-test", 123, TimeSpan.FromSeconds(5));

        await chat.GenerateAsync(request, CancellationToken.None);

        var span = Span("gemini.chat", root);
        Assert.Equal("llm", Tag(span, "langsmith.span.kind"));
        Assert.Equal("gemini-test", Tag(span, "gen_ai.request.model"));
        Assert.Equal("system", Tag(span, "gen_ai.prompt.0.role"));
        Assert.Equal("rules", Tag(span, "gen_ai.prompt.0.content"));
        Assert.Equal("user", Tag(span, "gen_ai.prompt.1.role"));
        Assert.Equal("question", Tag(span, "gen_ai.prompt.1.content"));
        Assert.Equal("the answer", Tag(span, "gen_ai.completion.0.content"));
        Assert.Equal(11, Tag(span, "gen_ai.usage.input_tokens"));
        Assert.Equal(7, Tag(span, "gen_ai.usage.output_tokens"));
        Assert.Equal(3, Tag(span, "gen_ai.usage.reasoning.output_tokens"));
    }

    [Fact]
    public async Task With_content_capture_off_the_prompt_and_the_answer_are_not_in_the_span_but_the_counts_are()
    {
        Telemetry.CaptureContent = false;
        using var root = Telemetry.StartRoot("test", Telemetry.Kinds.Chain)!;
        var chat = new GeminiChat(() => FakeChatClient.Saying("the answer", input: 11, output: 7), new GeminiSettings(), new GenerationGate(new ChatSettings()));
        var request = new ModelRequest([new(ChatRole.User, "question")], "gemini-test", 123, TimeSpan.FromSeconds(5));

        await chat.GenerateAsync(request, CancellationToken.None);

        var span = Span("gemini.chat", root);
        Assert.Null(Tag(span, "gen_ai.prompt.0.content"));
        Assert.Null(Tag(span, "gen_ai.completion.0.content"));
        Assert.Equal(11, Tag(span, "gen_ai.usage.input_tokens"));
    }

    [Fact]
    public async Task A_failed_model_call_marks_the_span_as_an_error_with_an_exception_event()
    {
        using var root = Telemetry.StartRoot("test", Telemetry.Kinds.Chain)!;
        var chat = new GeminiChat(() => FakeChatClient.Failing(new InvalidOperationException("no key")), new GeminiSettings(), new GenerationGate(new ChatSettings()));
        var request = new ModelRequest([new(ChatRole.User, "question")], "gemini-test", 123, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<Exception>(() => chat.GenerateAsync(request, CancellationToken.None));

        var span = Span("gemini.chat", root);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Contains(span.Events, e => e.Name == "exception");
    }

    [Fact]
    public async Task The_rewrite_step_reports_the_question_the_standalone_query_and_why_it_fell_back()
    {
        using var root = Telemetry.StartRoot("test", Telemetry.Kinds.Chain)!;
        var chat = new GeminiChat(() => FakeChatClient.Saying("How do I cancel a booking?"), new GeminiSettings(), new GenerationGate(new ChatSettings()));
        var rewriter = new QueryRewriter(chat, new GeminiSettings(), new ChatSettings());
        HistoryTurn[] history = [new("How do I create a booking?", "Use POST /bookings.", 10)];

        await rewriter.RewriteAsync(history, "and cancel it?", CancellationToken.None);

        var span = Span("rewrite_query", root);
        Assert.Equal("and cancel it?", Tag(span, "input.value"));
        Assert.Equal("How do I cancel a booking?", Tag(span, "output.value"));
        Assert.Equal(1, Tag(span, "langsmith.metadata.history_turns"));
        Assert.Equal(false, Tag(span, "langsmith.metadata.skipped"));
        Assert.Equal(true, Tag(span, "langsmith.metadata.changed"));
        Assert.Null(Tag(span, "langsmith.metadata.fallback_reason"));
        Assert.Equal("gemini.chat", Assert.Single(_spans, s => s.Parent == span).OperationName); // the model call is nested in it
    }

    [Fact]
    public void Without_a_listener_the_helpers_do_nothing_and_do_not_throw()
    {
        _listener.Dispose(); // no one listens to the source now (the exporter is not registered without a key)

        Activity? none = Telemetry.Start("x", Telemetry.Kinds.Chain);
        none.SetInput("text");
        none.SetMeta("k", 1);
        none.Fail(new Exception("boom"));

        Assert.Null(none);
    }
}
