using Microsoft.Extensions.AI;

namespace RagSystem.Tests.Gemini;

/// <summary>A chat client that answers from a function and remembers what it was sent. No network.</summary>
public sealed class FakeChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> reply) : IChatClient
{
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    /// <summary>Always answers with this text (and these token counts).</summary>
    public static FakeChatClient Saying(string text, long input = 10, long output = 5, long thinking = 0, ChatFinishReason? finish = null) =>
        new((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finish ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, ReasoningTokenCount = thinking },
        }));

    /// <summary>Always fails with this exception.</summary>
    public static FakeChatClient Failing(Exception error) => new((_, _, _) => throw error);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sent = messages.ToList();
        Calls.Add((sent, options));
        return reply(sent, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
