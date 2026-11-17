namespace RagSystem.Database;

/// <summary>The "Database" section of appsettings.json. The connection string itself is a secret and comes from <c>ConnectionStrings__Db</c>.</summary>
public sealed record DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations when the app starts. On for local use (Docker); off by default.</summary>
    public bool MigrateOnStartup { get; init; }
}
