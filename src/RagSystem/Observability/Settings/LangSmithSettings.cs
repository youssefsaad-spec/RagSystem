namespace RagSystem.Observability.Settings;

/// <summary>
/// The "LangSmith" section of appsettings.json. Traces leave the app through OpenTelemetry (OTLP over HTTP) and arrive in LangSmith.
/// Tracing is off until an API key is supplied. The key is NOT kept in appsettings.json: set the environment variable
/// <c>LangSmith__ApiKey</c> (or put it in the git-ignored appsettings.Local.json for local work).
/// </summary>
public sealed record LangSmithSettings
{
    public const string SectionName = "LangSmith";

    /// <summary>Turns tracing off without removing the key.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The OTLP base address of the LangSmith region, without <c>/v1/traces</c>: US <c>https://api.smith.langchain.com/otel</c>,
    /// EU <c>https://eu.api.smith.langchain.com/otel</c>, self-hosted <c>https://&lt;host&gt;/api/v1/otel</c>.
    /// </summary>
    public string Endpoint { get; init; } = "https://api.smith.langchain.com/otel";

    /// <summary>The LangSmith API key (sent as the <c>x-api-key</c> header). Empty here on purpose.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>The LangSmith project the traces land in. LangSmith creates it on the first trace.</summary>
    public string Project { get; init; } = "ragsystem";

    /// <summary>Only for an organisation-scoped key: the workspace to send to (the <c>X-Tenant-Id</c> header). Empty = the key's own workspace.</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>The service name on every span.</summary>
    public string ServiceName { get; init; } = "ragsystem";

    /// <summary>
    /// False sends the shape of each request only (steps, timings, counts, scores, ids) and leaves out the text: questions,
    /// prompts, answers and chunk contents.
    /// </summary>
    public bool CaptureContent { get; init; } = true;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>True when spans are really sent.</summary>
    public bool IsActive => Enabled && HasApiKey;

    /// <summary>
    /// The full address the exporter posts to. It is given to the exporter in code, and an address set in code is used exactly as
    /// written (only <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> gets the path added), so <c>/v1/traces</c> is added here.
    /// </summary>
    public string TracesUrl => Endpoint.TrimEnd('/') + "/v1/traces";

    /// <summary>The request headers in the exporter's <c>name=value,name=value</c> format.</summary>
    public string Headers
    {
        get
        {
            var headers = new List<string> { $"x-api-key={ApiKey.Trim()}", $"Langsmith-Project={Project.Trim()}" };
            if (!string.IsNullOrWhiteSpace(WorkspaceId))
                headers.Add($"X-Tenant-Id={WorkspaceId.Trim()}");
            return string.Join(',', headers);
        }
    }

    /// <summary>Returns this object, or throws when a value cannot work. A missing key is not an error: tracing is just off.</summary>
    public LangSmithSettings EnsureValid()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{SectionName}:Endpoint must be an http or https address, but it is '{Endpoint}'.");
        if (Endpoint.TrimEnd('/').EndsWith("/v1/traces", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{SectionName}:Endpoint must not end with /v1/traces (it is added for you), but it is '{Endpoint}'.");
        if (string.IsNullOrWhiteSpace(Project))
            throw new InvalidOperationException($"{SectionName}:Project must not be empty.");
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new InvalidOperationException($"{SectionName}:ServiceName must not be empty.");

        // The header list is "name=value,name=value", so a comma or an equals sign in a value would split it wrongly.
        foreach (var (name, value) in new[] { ("Project", Project), ("ApiKey", ApiKey), ("WorkspaceId", WorkspaceId) })
            if (value.AsSpan().IndexOfAny(',', '=') >= 0)
                throw new InvalidOperationException($"{SectionName}:{name} must not contain ',' or '='.");
        return this;
    }
}
