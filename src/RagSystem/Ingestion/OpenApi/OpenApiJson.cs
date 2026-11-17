using Microsoft.OpenApi;

namespace RagSystem.Ingestion.OpenApi;

/// <summary>Writes one part of an OpenAPI document (an operation or a schema) back as JSON text.</summary>
public static class OpenApiJson
{
    /// <summary>
    /// <c>$ref</c>s are kept as short references and not expanded. Expanding them repeats the shared schemas in
    /// every operation (177,000 tokens for the V3 file, see EXCLUDED.md).
    /// </summary>
    public static string Write(IOpenApiSerializable part)
    {
        using var text = new StringWriter();
        var writer = new OpenApiJsonWriter(text, new OpenApiJsonWriterSettings { InlineLocalReferences = false });
        part.SerializeAsV3(writer);
        return text.ToString();
    }
}
