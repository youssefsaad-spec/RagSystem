namespace RagSystem.Database.Entities;

/// <summary>
/// A chat between one client and the documentation of one version. The row is created when the first exchange is saved,
/// so a request that fails leaves nothing behind.
/// </summary>
public sealed class Conversation
{
    public Guid Id { get; set; }

    /// <summary>The client that owns it. Null while the endpoints are open (before F29 there are no clients); then there is no foreign key either.</summary>
    public Guid? ClientId { get; set; }

    /// <summary>"v2" or "v3", lower case.</summary>
    public string Version { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastMessageAt { get; set; }
}
