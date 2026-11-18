using System.Diagnostics;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Embeddings.Services;
using RagSystem.Gemini.Models;
using RagSystem.Gemini.Settings;
using RagSystem.Observability;

namespace RagSystem.Gemini.Services;

/// <summary>
/// The chat model (F20): sends messages to Gemini through the Microsoft.Extensions.AI client, one at a time through the
/// <see cref="GenerationGate"/> (F21), and returns the text, the token counts and whether it was cut off. Every failure is an
/// <see cref="EmbeddingException"/> with a failure kind (the same kinds as the embeddings), which the controller turns into 503 or 502 (F24).
/// </summary>
/// <param name="client">Gives the chat client. It is asked for on first use, so the app starts without an API key.</param>
/// <param name="logger">Gets one warning line per retry (the reason Gemini gave, no question or answer text).</param>
/// <param name="wait">How a retry waits. Tests pass a function that does not really wait.</param>
public sealed class GeminiChat(
    Func<IChatClient> client,
    GeminiSettings settings,
    GenerationGate gate,
    ILogger? logger = null,
    Func<TimeSpan, CancellationToken, Task>? wait = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? Task.Delay;

    /// <exception cref="EmbeddingException">The call failed, timed out, or there was no free place at the gate.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public Task<ModelReply> GenerateAsync(ModelRequest request, CancellationToken cancellationToken) =>
        Telemetry.TraceAsync("gemini.chat", Telemetry.Kinds.Llm, async span =>
        {
            // The attribute names LangSmith reads for a model call (OpenTelemetry GenAI): the prompt messages, the settings, then
            // the completion and the token counts. The span covers the wait at the gate and the retries too.
            span?.SetTag("gen_ai.system", "google_genai");
            span?.SetTag("gen_ai.operation.name", "chat");
            span?.SetTag("gen_ai.request.model", request.Model);
            span?.SetTag("gen_ai.request.temperature", settings.Temperature);
            span?.SetTag("gen_ai.request.max_tokens", request.MaxOutputTokens);
            span.SetMeta("thinking_budget", settings.ThinkingBudget);
            span.SetMeta("max_retries", settings.ChatRetries);
            if (Telemetry.CaptureContent)
                for (var i = 0; i < request.Messages.Count; i++)
                {
                    span?.SetTag($"gen_ai.prompt.{i}.role", request.Messages[i].Role.Value);
                    span?.SetTag($"gen_ai.prompt.{i}.content", request.Messages[i].Text);
                }

            var reply = await gate.RunAsync(request.Timeout, token => CallAsync(request, token), cancellationToken);

            span?.SetTag("gen_ai.response.model", reply.Model);
            span?.SetTag("gen_ai.usage.input_tokens", reply.InputTokens);
            span?.SetTag("gen_ai.usage.output_tokens", reply.OutputTokens);
            span?.SetTag("gen_ai.usage.reasoning.output_tokens", reply.ThinkingTokens);
            span.SetMeta("truncated", reply.Truncated);
            if (Telemetry.CaptureContent)
            {
                span?.SetTag("gen_ai.completion.0.role", "assistant");
                span?.SetTag("gen_ai.completion.0.content", reply.Text);
            }

            return reply;
        });

    private async Task<ModelReply> CallAsync(ModelRequest request, CancellationToken token)
    {
        var options = new ChatOptions
        {
            ModelId = request.Model,
            Temperature = (float)settings.Temperature,
            MaxOutputTokens = request.MaxOutputTokens,
            // Thinking tokens count against the output limit; the budget is a setting (0 = off) so answers are not cut short by thinking.
            RawRepresentationFactory = _ => new GenerateContentConfig { ThinkingConfig = new ThinkingConfig { ThinkingBudget = settings.ThinkingBudget } },
        };

        ChatResponse response;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                response = await client().GetResponseAsync(request.Messages, options, token);
                break;
            }
            catch (OperationCanceledException)
            {
                throw; // the gate tells a timeout from the caller's cancel
            }
            catch (Exception error)
            {
                var failure = GuardedEmbeddingGenerator.Classify("Gemini", error);

                // Only a passing problem (Gemini overloaded, 5xx, connection) is worth another try. A bad key, a missing model
                // or a quota (429) will not change in a few seconds. The waits count against the call's timeout.
                if (failure.Kind != EmbeddingFailureKind.Transient || attempt >= settings.ChatRetries)
                    throw failure;

                var delay = TimeSpan.FromSeconds(settings.RetryDelaySeconds * Math.Pow(2, attempt));
                // Shows on the model span in LangSmith as an event, so a slow answer can be traced to its retries.
                Activity.Current?.AddEvent(new ActivityEvent("retry", tags: new ActivityTagsCollection
                {
                    ["attempt"] = attempt + 1,
                    ["reason"] = failure.Message,
                    ["delay_seconds"] = delay.TotalSeconds,
                }));
                logger?.LogWarning("gemini: attempt {Attempt} failed ({Reason}); retrying in {Delay} s.", attempt + 1, failure.Message, delay.TotalSeconds);
                await _wait(delay, token);
            }
        }

        return new ModelReply(
            response.Text ?? "",
            (int)(response.Usage?.InputTokenCount ?? 0),
            (int)(response.Usage?.OutputTokenCount ?? 0),
            (int)(response.Usage?.ReasoningTokenCount ?? 0),
            response.FinishReason == ChatFinishReason.Length,
            response.ModelId ?? request.Model);
    }
}
