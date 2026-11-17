namespace RagSystem.Database.Entities;

public enum DocumentStatus
{
    /// <summary>Uploaded; its chunks are being embedded and stored.</summary>
    Processing,

    /// <summary>Its chunks are stored and can be searched.</summary>
    Ready,

    /// <summary>Storing failed; <see cref="Document.Error"/> says why.</summary>
    Failed,
}
