namespace RagSystem.Database.Entities;

/// <summary>One question or one answer of a conversation.</summary>
public sealed class Message
{
    public Guid Id { get; set; }

    /// <summary>The conversation it belongs to. Deleting the conversation deletes its messages.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>"user" or "assistant" (see <c>MessageRoles</c>).</summary>
    public string Role { get; set; } = "";

    /// <summary>The question or the answer (without its Sources line). Questions are stored with personal data removed once F33 exists.</summary>
    public string Content { get; set; } = "";

    /// <summary>On a user message: the text the search used (the question, or its rewrite). Null on an answer.</summary>
    public string? RetrievalQuery { get; set; }

    /// <summary>On an answer: the model name the service reported (it can include a version suffix).</summary>
    public string? ModelVersion { get; set; }

    public int EstTokens { get; set; }

    /// <summary>Token counts the model service reported for this answer. 0 on a question.</summary>
    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int ThinkingTokens { get; set; }

    /// <summary>On an answer: the model that was asked (from settings). Null on a question and on a "not covered" answer (no model was called).</summary>
    public string? Model { get; set; }

    /// <summary>On an answer: the chunks it is based on. These are ids, not copies, so a deleted document simply drops out of the citations.</summary>
    public Guid[] CitedChunkIds { get; set; } = [];

    public bool CacheHit { get; set; }

    /// <summary>JSON object of notes (for example <c>{"rewrite":"timeout"}</c> or <c>{"sources_missing":"true"}</c>). Null when there are none.</summary>
    public string? GuardFlags { get; set; }

    /// <summary>The trace of the request (F38). Null until it exists.</summary>
    public string? TraceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
