using Microsoft.Extensions.AI;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Retrieval.Models;

namespace RagSystem.Chat.Services;

/// <summary>
/// Builds the messages sent to the model (F22): the rules, then the earlier turns, then one user message holding the
/// documentation (marked as data) and the question.
/// </summary>
public sealed class PromptBuilder
{
    /// <param name="standaloneQuestion">The question rewritten from the conversation (F27), when it differs from <paramref name="question"/>. Otherwise null.</param>
    /// <param name="history">Earlier question-and-answer pairs, oldest first.</param>
    public IReadOnlyList<ChatMessage> Build(
        string version,
        string question,
        string? standaloneQuestion,
        IReadOnlyList<RetrievedSource> sources,
        IReadOnlyList<HistoryTurn> history)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, PromptRules.System) };
        foreach (var turn in history)
        {
            messages.Add(new ChatMessage(ChatRole.User, turn.Question));
            messages.Add(new ChatMessage(ChatRole.Assistant, turn.Answer));
        }

        messages.Add(new ChatMessage(ChatRole.User, UserMessage(version, question, standaloneQuestion, sources)));
        return messages;
    }

    private static string UserMessage(string version, string question, string? standaloneQuestion, IReadOnlyList<RetrievedSource> sources)
    {
        var text = new System.Text.StringBuilder();
        text.Append("<documentation version=\"").Append(version).Append("\">\n");
        foreach (var source in sources)
            text.Append("<source id=\"").Append(source.Label).Append("\">\n").Append(Defang(source.Content)).Append("\n</source>\n");
        text.Append("</documentation>\n\nQuestion: ").Append(question);

        if (!string.IsNullOrWhiteSpace(standaloneQuestion) && standaloneQuestion != question)
            text.Append("\n(The same question made standalone from the conversation: ").Append(standaloneQuestion).Append(')');
        return text.ToString();
    }

    /// <summary>A source cannot close the wrapper tags: <c>&lt;/source&gt;</c> and <c>&lt;/documentation&gt;</c> are replaced with look-alikes.</summary>
    private static string Defang(string content) => content
        .Replace("</source>", "<\\/source>", StringComparison.OrdinalIgnoreCase)
        .Replace("</documentation>", "<\\/documentation>", StringComparison.OrdinalIgnoreCase);
}
