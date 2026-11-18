using RagSystem.Retrieval.Models;

namespace RagSystem.Chat.Models;

/// <summary>
/// A model reply split into the answer text and the sources it used (F23).
/// </summary>
/// <param name="Text">The answer without its <c>Sources:</c> line.</param>
/// <param name="Cited">The sources the answer is based on, in the order the model named them, no duplicates.</param>
/// <param name="NotCovered">The model wrote <c>Sources: none</c>: it says the documentation does not cover the question.</param>
/// <param name="SourcesMissing">The reply had no usable <c>Sources:</c> line, so the first sources were cited instead. Never cache such an answer.</param>
public sealed record ParsedAnswer(string Text, IReadOnlyList<RetrievedSource> Cited, bool NotCovered, bool SourcesMissing)
{
    /// <summary>Nothing is left of the reply once the sources line is taken off: <c>502 empty_answer</c>.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
