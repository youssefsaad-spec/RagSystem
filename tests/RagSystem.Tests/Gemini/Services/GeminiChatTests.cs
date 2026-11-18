using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using RagSystem.Chat.Settings;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Gemini.Models;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;

namespace RagSystem.Tests.Gemini.Services;

public class GeminiChatTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages =
        [new(ChatRole.System, "rules"), new(ChatRole.User, "question")];

    private static ModelRequest Request(string model = "gemini-test", int maxOutput = 123) =>
        new(Messages, model, maxOutput, TimeSpan.FromSeconds(5));

    /// <param name="waits">Collects the waits the chat asked for; nothing really waits.</param>
    private static GeminiChat ChatOn(IChatClient client, GeminiSettings? settings = null, List<TimeSpan>? waits = null) =>
        new(() => client, settings ?? new GeminiSettings(), new GenerationGate(new ChatSettings()), wait: (delay, _) =>
        {
            waits?.Add(delay);
            return Task.CompletedTask;
        });

    private static ApiException Status(int status) => new("failed", status, "STATUS", null!);

    private static FakeChatClient FailingThenSaying(int failures, int status, string text)
    {
        var calls = 0;
        return new FakeChatClient((_, _, _) => ++calls <= failures
            ? throw Status(status)
            : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));
    }

    [Fact]
    public async Task An_overloaded_model_is_tried_again_after_2_then_4_seconds_and_the_answer_is_returned()
    {
        var client = FailingThenSaying(failures: 2, status: 503, text: "finally");
        var waits = new List<TimeSpan>();

        var reply = await ChatOn(client, waits: waits).GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("finally", reply.Text);
        Assert.Equal(3, client.Calls.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task After_the_retries_are_used_up_the_failure_is_transient_and_there_were_three_calls()
    {
        var client = FailingThenSaying(failures: 99, status: 503, text: "never");

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => ChatOn(client).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.Equal(3, client.Calls.Count);
    }

    [Theory]
    [InlineData(429)] // a quota will not clear in seconds, and every try costs quota
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(400)]
    public async Task A_failure_that_waiting_will_not_fix_is_not_retried(int status)
    {
        var client = FailingThenSaying(failures: 99, status: status, text: "never");

        await Assert.ThrowsAsync<EmbeddingException>(() => ChatOn(client).GenerateAsync(Request(), CancellationToken.None));

        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task With_chat_retries_set_to_0_there_is_one_call()
    {
        var client = FailingThenSaying(failures: 99, status: 503, text: "never");

        await Assert.ThrowsAsync<EmbeddingException>(
            () => ChatOn(client, new GeminiSettings { ChatRetries = 0 }).GenerateAsync(Request(), CancellationToken.None));

        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task A_timeout_while_waiting_between_retries_is_a_transient_timeout()
    {
        var client = FailingThenSaying(failures: 99, status: 503, text: "never");
        var chat = new GeminiChat(() => client, new GeminiSettings { RetryDelaySeconds = 30 }, new GenerationGate(new ChatSettings()));
        var request = Request() with { Timeout = TimeSpan.FromMilliseconds(300) };

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => chat.GenerateAsync(request, CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Single(client.Calls); // it never got to a second try
    }

    [Fact]
    public async Task The_reply_carries_the_text_the_token_counts_and_the_model()
    {
        var client = FakeChatClient.Saying("An answer.\nSources: S1", input: 111, output: 22, thinking: 3);

        var reply = await ChatOn(client).GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("An answer.\nSources: S1", reply.Text);
        Assert.Equal((111, 22, 3), (reply.InputTokens, reply.OutputTokens, reply.ThinkingTokens));
        Assert.False(reply.Truncated);
        Assert.Equal("gemini-test", reply.Model);
    }

    [Fact]
    public async Task A_reply_that_stopped_at_the_output_limit_is_marked_truncated()
    {
        var client = FakeChatClient.Saying("cut of", finish: ChatFinishReason.Length);

        var reply = await ChatOn(client).GenerateAsync(Request(), CancellationToken.None);

        Assert.True(reply.Truncated);
    }

    [Fact]
    public async Task The_model_limit_temperature_and_thinking_budget_are_passed_on_and_the_messages_are_sent_as_given()
    {
        var client = FakeChatClient.Saying("ok");
        var settings = new GeminiSettings { Temperature = 0.3, ThinkingBudget = 0 };

        await ChatOn(client, settings).GenerateAsync(Request("gemini-rewrite", maxOutput: 300), CancellationToken.None);

        var (sent, options) = Assert.Single(client.Calls);
        Assert.Equal(["rules", "question"], sent.Select(m => m.Text));
        Assert.Equal("gemini-rewrite", options!.ModelId);
        Assert.Equal(300, options.MaxOutputTokens);
        Assert.Equal(0.3f, options.Temperature);
        var config = Assert.IsType<GenerateContentConfig>(options.RawRepresentationFactory!(client));
        Assert.Equal(0, config.ThinkingConfig!.ThinkingBudget);
    }

    [Fact]
    public async Task A_missing_token_count_is_zero()
    {
        var client = new FakeChatClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi"))));

        var reply = await ChatOn(client).GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal((0, 0, 0), (reply.InputTokens, reply.OutputTokens, reply.ThinkingTokens));
    }

    [Theory]
    [InlineData(429, EmbeddingFailureKind.RateLimited)]
    [InlineData(503, EmbeddingFailureKind.Transient)]
    [InlineData(500, EmbeddingFailureKind.Transient)]
    [InlineData(400, EmbeddingFailureKind.Fatal)]
    [InlineData(401, EmbeddingFailureKind.Fatal)]
    [InlineData(404, EmbeddingFailureKind.Fatal)]
    public async Task A_Gemini_error_is_classified_by_its_http_status(int status, EmbeddingFailureKind kind)
    {
        var client = FakeChatClient.Failing(new ApiException("failed", status, "STATUS", null!));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => ChatOn(client).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(kind, error.Kind);
    }

    [Fact]
    public async Task A_connection_failure_is_transient()
    {
        var client = FakeChatClient.Failing(new HttpRequestException("refused"));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => ChatOn(client).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }

    [Fact]
    public async Task A_missing_api_key_is_fatal_and_the_app_did_not_need_the_client_before()
    {
        var chat = new GeminiChat(
            () => throw new InvalidOperationException("Gemini:ApiKey is not set."),
            new GeminiSettings(), new GenerationGate(new ChatSettings()));

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => chat.GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Fatal, error.Kind);
        Assert.Contains("Gemini:ApiKey", error.Message);
    }

    [Fact]
    public async Task A_call_that_takes_longer_than_the_timeout_is_transient()
    {
        var client = new FakeChatClient(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return new ChatResponse(); });
        var request = Request() with { Timeout = TimeSpan.FromMilliseconds(100) };

        var error = await Assert.ThrowsAsync<EmbeddingException>(() => ChatOn(client).GenerateAsync(request, CancellationToken.None));

        Assert.Equal(EmbeddingFailureKind.Transient, error.Kind);
    }
}
