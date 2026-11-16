namespace RagSystem.Ingestion;

/// <summary>The kind of a chunk. These exact strings are stored in the database.</summary>
public static class ChunkTypes
{
    public const string Endpoint = "endpoint";
    public const string EndpointSpec = "endpoint_spec";
    public const string ApiOverview = "api_overview";
    public const string Workflow = "workflow";
    public const string DataModel = "data_model";
    public const string Faq = "faq";
    public const string Enum = "enum";
    public const string Errors = "errors";
    public const string Glossary = "glossary";
    public const string Guide = "guide";
}
