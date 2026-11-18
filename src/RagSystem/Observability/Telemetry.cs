using System.Diagnostics;
using System.Text.Json;
using RagSystem.Retrieval.Models;

namespace RagSystem.Observability;

/// <summary>
/// The app's traces (OpenTelemetry, shown in LangSmith). Every step of a chat turn is a span of the one <see cref="Source"/>.
/// With no exporter registered (no LangSmith key) nobody listens to the source, <c>StartActivity</c> returns null and every
/// helper below does nothing, so the code that uses them needs no "is tracing on" checks.
/// </summary>
/// <remarks>
/// LangSmith reads these attribute names: <c>langsmith.span.kind</c> (the run type), <c>input.value</c> and <c>output.value</c>,
/// <c>gen_ai.*</c> for model calls, <c>retrieval.documents.N.*</c> for retrievers and <c>langsmith.metadata.*</c> for free data.
/// </remarks>
public static class Telemetry
{
    public const string SourceName = "RagSystem";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// False keeps question, prompt, answer and chunk text out of the spans (set from <c>LangSmith:CaptureContent</c> at start).
    /// Counts, scores, ids and timings are always sent.
    /// </summary>
    public static bool CaptureContent { get; set; } = true;

    /// <summary>The LangSmith run types (<c>langsmith.span.kind</c>).</summary>
    public static class Kinds
    {
        public const string Chain = "chain";
        public const string Llm = "llm";
        public const string Retriever = "retriever";
        public const string Embedding = "embedding";
        public const string Prompt = "prompt";
        public const string Parser = "parser";
        public const string Tool = "tool";
    }

    /// <summary>A span that is a child of the current one.</summary>
    public static Activity? Start(string name, string kind)
    {
        var activity = Source.StartActivity(name);
        activity?.SetTag("langsmith.span.kind", kind);
        return activity;
    }

    /// <summary>
    /// The first span of a trace. It starts a new trace on purpose: ASP.NET Core may already have an (unrecorded) request activity,
    /// and a span under an unrecorded parent is dropped by the default sampler.
    /// </summary>
    public static Activity? StartRoot(string name, string kind)
    {
        // A default parent context does NOT mean "no parent": .NET then falls back to Activity.Current. So the ambient activity is
        // cleared for the moment the span starts (Activity.Current is per async flow, so the caller's own flow is not affected).
        if (!Source.HasListeners())
            return null; // tracing is off: leave the request's own activity alone

        Activity.Current = null;
        var activity = Source.StartActivity(name);
        activity?.SetTag("langsmith.span.kind", kind);
        return activity;
    }

    /// <summary>Runs <paramref name="work"/> inside a span, and marks the span as failed when it throws.</summary>
    public static async Task<T> TraceAsync<T>(string name, string kind, Func<Activity?, Task<T>> work)
    {
        using var activity = Start(name, kind);
        try
        {
            return await work(activity);
        }
        catch (Exception error)
        {
            activity.Fail(error);
            throw;
        }
    }

    /// <summary>The same for work that is not async.</summary>
    public static T Trace<T>(string name, string kind, Func<Activity?, T> work)
    {
        using var activity = Start(name, kind);
        try
        {
            return work(activity);
        }
        catch (Exception error)
        {
            activity.Fail(error);
            throw;
        }
    }

    /// <summary>The span's input, as LangSmith shows it. Text, so it follows <see cref="CaptureContent"/>.</summary>
    public static void SetInput(this Activity? activity, string? value)
    {
        if (CaptureContent && value is not null)
            activity?.SetTag("input.value", value);
    }

    /// <summary>The span's output, as LangSmith shows it. Text, so it follows <see cref="CaptureContent"/>.</summary>
    public static void SetOutput(this Activity? activity, string? value)
    {
        if (CaptureContent && value is not null)
            activity?.SetTag("output.value", value);
    }

    /// <summary>The span's input or output as structured data (JSON). The caller keeps text out of it when <see cref="CaptureContent"/> is false.</summary>
    public static void SetInputJson(this Activity? activity, object value) => activity?.SetTag("input.value", JsonSerializer.Serialize(value));

    /// <inheritdoc cref="SetInputJson"/>
    public static void SetOutputJson(this Activity? activity, object value) => activity?.SetTag("output.value", JsonSerializer.Serialize(value));

    /// <summary>Free data shown under "Metadata" in LangSmith (filterable). A null value is skipped.</summary>
    public static void SetMeta(this Activity? activity, string key, object? value)
    {
        if (activity is not null && value is not null)
            activity.SetTag($"langsmith.metadata.{key}", value is Guid or Enum ? value.ToString() : value);
    }

    /// <summary>
    /// The chunks of a retriever span, in the order given. LangSmith shows them as documents with their metadata. The text of a
    /// chunk follows <see cref="CaptureContent"/>; <paramref name="previewChars"/> cuts long text (0 = send it all).
    /// </summary>
    public static void SetDocuments(this Activity? activity, IEnumerable<(string Content, object Metadata)> documents, int previewChars = 0)
    {
        if (activity is null)
            return;

        var index = 0;
        foreach (var (content, metadata) in documents)
        {
            if (CaptureContent)
                activity.SetTag($"retrieval.documents.{index}.document.content", Preview(content, previewChars));
            activity.SetTag($"retrieval.documents.{index}.document.metadata", JsonSerializer.Serialize(metadata));
            index++;
        }
    }

    /// <summary>Marks the span as failed: the status, and an <c>exception</c> event, which LangSmith shows as the run's error.</summary>
    public static void Fail(this Activity? activity, Exception error)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, error.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = error.GetType().FullName,
            ["exception.message"] = error.Message,
        }));
    }

    /// <summary>How a chunk is described in the spans. Never includes the embedding vector.</summary>
    public static object DescribeChunk(ChunkCandidate c) => new
    {
        chunk_id = c.ChunkId,
        document = c.DocumentTitle,
        ordinal = c.Ordinal,
        score = Math.Round(c.Score, 4),
        est_tokens = c.EstTokens,
    };

    private static string Preview(string text, int max) => max > 0 && text.Length > max ? text[..max] + "…" : text;
}
