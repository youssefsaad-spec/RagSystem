namespace RagSystem.Database;

public enum DocumentStatus
{
    /// <summary>Uploaded; its chunks are being embedded and stored.</summary>
    Processing,

    /// <summary>Its chunks are stored and can be searched.</summary>
    Ready,

    /// <summary>Storing failed; <see cref="Document.Error"/> says why.</summary>
    Failed,
}

/// <summary>One uploaded file. Same <see cref="Version"/> and <see cref="FileName"/> means the same document (a re-upload replaces it).</summary>
public sealed class Document
{
    public Guid Id { get; set; }

    /// <summary>"v2" or "v3", lower case.</summary>
    public string Version { get; set; } = "";

    public string FileName { get; set; } = "";

    /// <summary>The name shown in citations.</summary>
    public string Title { get; set; } = "";

    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;

    public int ChunkCount { get; set; }

    /// <summary>Why the document is Failed. Null otherwise.</summary>
    public string? Error { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }
}
