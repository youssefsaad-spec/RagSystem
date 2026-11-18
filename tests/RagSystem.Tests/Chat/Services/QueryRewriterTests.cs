using Google.GenAI;
using Microsoft.Extensions.AI;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Chat.Services;
using RagSystem.Chat.Settings;
using RagSystem.Gemini.Services;
using RagSystem.Gemini.Settings;
using RagSystem.Tests.Gemini;

namespace RagSystem.Tests.Chat.Services;

public class QueryRewriterTests
{
    private static readonly GeminiSettings Gemini = new() { ChatModel = "chat-model", RewriteModel = "rewrite-model" };

    private static QueryRewriter RewriterOn(FakeChatClient client, ChatSettings? settings = null)
    {
        settings ??= new ChatSettings();
        return new QueryRewriter(new GeminiChat(() => client, Gemini, new GenerationGate(settings)), Gemini, settings);
    }

    private static HistoryTurn Turn(string question, string answer = "An answer.") => new(question, answer, 20);

    [Fact]
    public async Task A_first_message_is_used_as_it_is_with_no_model_call()
    {
        var client = FakeChatClient.Saying("should not be called");

        var result = await RewriterOn(client).RewriteAsync([], "What does FareCheck do?", CancellationToken.None);

        Assert.Equal(new RewriteResult("What does FareCheck do?", true, null), result);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task A_follow_up_is_rewritten_by_the_rewrite_model_with_the_output_limit_and_no_answer_asked_for()
    {
        var client = FakeChatClient.Saying("What does the FareCheck response contain?");

        var result = await RewriterOn(client).RewriteAsync([Turn("What does FareCheck do?")], "and what does its response contain?", CancellationToken.None);

        Assert.Equal(new RewriteResult("What does the FareCheck response contain?", true, null), result);
        var (messages, options) = Assert.Single(client.Calls);
        Assert.Equal("rewrite-model", options!.ModelId);
        Assert.Equal(300, options.MaxOutputTokens);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("standalone question", messages[0].Text);
        Assert.Contains("User: What does FareCheck do?", messages[1].Text);
        Assert.EndsWith("Last message: and what does its response contain?", messages[1].Text);
    }

    [Fact]
    public async Task Only_the_last_three_turns_are_sent_and_long_answers_are_cut_to_400_characters()
    {
        var client = FakeChatClient.Saying("Standalone.");
        var history = new[] { Turn("first"), Turn("second"), Turn("third", new string('x', 900)), Turn("fourth") };

        await RewriterOn(client).RewriteAsync(history, "next", CancellationToken.None);

        var text = client.Calls[0].Messages[1].Text;
        Assert.DoesNotContain("User: first", text);
        Assert.Contains("User: second", text);
        Assert.Contains("User: fourth", text);
        Assert.Contains(new string('x', 400) + "...", text);
        Assert.DoesNotContain(new string('x', 401), text);
    }

    [Theory]
    [InlineData("What is FareCheck?", "What is FareCheck?")]
    [InlineData("\"What is FareCheck?\"", "What is FareCheck?")]
    [InlineData("“What is FareCheck?”", "What is FareCheck?")]
    [InlineData("Standalone question: What is FareCheck?", "What is FareCheck?")]
    [InlineData("standalone question: \"What is FareCheck?\"", "What is FareCheck?")]
    [InlineData("**Standalone question:** What is FareCheck?", "What is FareCheck?")]
    [InlineData("\n\n  What is FareCheck?  \nThis is an explanation I was told not to write.", "What is FareCheck?")]
    public async Task The_reply_is_cleaned_to_its_first_non_empty_line_without_quotes_or_a_prefix(string reply, string expected)
    {
        var result = await RewriterOn(FakeChatClient.Saying(reply)).RewriteAsync([Turn("q")], "m", CancellationToken.None);

        Assert.Equal(expected, result.Query);
        Assert.True(result.IsStandalone);
        Assert.Null(result.FallbackReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("\"\"")]
    public async Task An_empty_reply_is_invalid_and_the_fallback_query_is_used(string reply)
    {
        var result = await RewriterOn(FakeChatClient.Saying(reply)).RewriteAsync([Turn("What does FareCheck do?")], "and its response?", CancellationToken.None);

        Assert.Equal(new RewriteResult("What does FareCheck do? and its response?", false, RewriteReasons.InvalidOutput), result);
    }

    [Fact]
    public async Task A_reply_over_300_characters_is_invalid()
    {
        var result = await RewriterOn(FakeChatClient.Saying(new string('a', 301))).RewriteAsync([Turn("q1")], "q2", CancellationToken.None);

        Assert.Equal("q1 q2", result.Query);
        Assert.Equal(RewriteReasons.InvalidOutput, result.FallbackReason);
    }

    [Fact]
    public async Task A_reply_of_exactly_300_characters_is_valid()
    {
        var result = await RewriterOn(FakeChatClient.Saying(new string('a', 300))).RewriteAsync([Turn("q1")], "q2", CancellationToken.None);

        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public async Task The_fallback_uses_the_previous_question_not_an_older_one()
    {
        var result = await RewriterOn(FakeChatClient.Saying("")).RewriteAsync([Turn("older"), Turn("previous")], "current", CancellationToken.None);

        Assert.Equal("previous current", result.Query);
    }

    [Theory]
    [InlineData(429, RewriteReasons.RateLimited)]
    [InlineData(503, RewriteReasons.Error)]
    [InlineData(401, RewriteReasons.Error)]
    public async Task A_model_failure_falls_back_with_its_reason(int status, string reason)
    {
        var client = FakeChatClient.Failing(new ApiException("failed", status, "STATUS", null!));

        var result = await RewriterOn(client).RewriteAsync([Turn("q1")], "q2", CancellationToken.None);

        Assert.Equal(new RewriteResult("q1 q2", false, reason), result);
    }

    [Fact]
    public async Task A_rewrite_that_takes_too_long_falls_back_with_the_timeout_reason()
    {
        var client = new FakeChatClient(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return new ChatResponse(); });
        var settings = new ChatSettings { RewriteTimeoutSeconds = 1 };

        var result = await RewriterOn(client, settings).RewriteAsync([Turn("q1")], "q2", CancellationToken.None);

        Assert.Equal(new RewriteResult("q1 q2", false, RewriteReasons.Timeout), result);
    }

    [Fact]
    public async Task Waiting_for_the_gate_counts_against_the_rewrite_timeout()
    {
        var settings = new ChatSettings { RewriteTimeoutSeconds = 1 };
        using var gate = new GenerationGate(settings);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var busy = gate.RunAsync(TimeSpan.FromSeconds(30), async _ => { started.SetResult(); await release.Task; return 0; }, CancellationToken.None);
        await started.Task;
        var client = FakeChatClient.Saying("never asked");
        var rewriter = new QueryRewriter(new GeminiChat(() => client, Gemini, gate), Gemini, settings);

        var result = await rewriter.RewriteAsync([Turn("q1")], "q2", CancellationToken.None);

        Assert.Equal(RewriteReasons.Timeout, result.FallbackReason);
        Assert.Empty(client.Calls);
        release.SetResult();
        await busy;
    }

    [Fact]
    public async Task When_the_caller_cancels_the_cancellation_is_not_swallowed_into_a_fallback()
    {
        var client = new FakeChatClient(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return new ChatResponse(); });
        using var cancel = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RewriterOn(client).RewriteAsync([Turn("q1")], "q2", cancel.Token));
    }
}
