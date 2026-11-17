namespace RagSystem.ApiDocs;

/// <summary>The "OpenApi" section of appsettings.json: whether the API description and the Swagger page are served.</summary>
public sealed record ApiDocsSettings
{
    public const string SectionName = "OpenApi";

    /// <summary>Serves <c>/openapi/v1.json</c> and the Swagger page at <c>/swagger</c>, both without a key. On for local use only.</summary>
    public bool Enabled { get; init; }
}
