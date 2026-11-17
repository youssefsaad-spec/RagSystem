using RagSystem.Database;

namespace RagSystem.Gemini.Settings;

/// <summary>
/// The "Gemini" section of appsettings.json. The chat and query-rewrite model runs on the Gemini API instead of a local model.
/// The API key is NOT kept in appsettings.json: set it with the environment variable <c>Gemini__ApiKey</c> or with
/// <c>dotnet user-secrets</c>.
/// </summary>
public sealed record GeminiSettings
{
    public const string SectionName = "Gemini";

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com";

    /// <summary>The API key. Empty here on purpose; supply it through the environment or user secrets.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>The model that writes the answers.</summary>
    public string ChatModel { get; init; } = "gemini-2.5-flash";

    /// <summary>The model that rewrites a follow-up question into a standalone one.</summary>
    public string RewriteModel { get; init; } = "gemini-2.5-flash";

    /// <summary>The model that makes the embeddings. It can shorten its vectors to <see cref="EmbeddingDimensions"/>.</summary>
    public string EmbeddingModel { get; init; } = "gemini-embedding-001";

    /// <summary>Must equal the size of the database column (1,024); the app refuses to start otherwise.</summary>
    public int EmbeddingDimensions { get; init; } = RagDbContext.EmbeddingDimensions;

    public double Temperature { get; init; } = 0.3;

    public int MaxOutputTokens { get; init; } = 3000;

    /// <summary>How long one call may take before it counts as a transient failure.</summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>True when a key was supplied. The app starts without one; only chat calls need it.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Returns this object, or throws when a value cannot work. A missing key is not an error here.</summary>
    public GeminiSettings EnsureValid()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{SectionName}:BaseUrl must be an http or https address, but it is '{BaseUrl}'.");
        if (string.IsNullOrWhiteSpace(ChatModel))
            throw new InvalidOperationException($"{SectionName}:ChatModel must not be empty.");
        if (string.IsNullOrWhiteSpace(RewriteModel))
            throw new InvalidOperationException($"{SectionName}:RewriteModel must not be empty.");
        if (string.IsNullOrWhiteSpace(EmbeddingModel))
            throw new InvalidOperationException($"{SectionName}:EmbeddingModel must not be empty.");
        if (EmbeddingDimensions != RagDbContext.EmbeddingDimensions)
            throw new InvalidOperationException(
                $"{SectionName}:EmbeddingDimensions is {EmbeddingDimensions}, but the database column holds {RagDbContext.EmbeddingDimensions}. " +
                "Another size needs a migration and a full re-embed.");
        if (Temperature is < 0 or > 2)
            throw new InvalidOperationException($"{SectionName}:Temperature must be between 0 and 2, but it is {Temperature}.");
        if (MaxOutputTokens <= 0)
            throw new InvalidOperationException($"{SectionName}:MaxOutputTokens must be above 0, but it is {MaxOutputTokens}.");
        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException($"{SectionName}:TimeoutSeconds must be above 0, but it is {TimeoutSeconds}.");
        return this;
    }
}
