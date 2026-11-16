using RagSystem.Ingestion;

namespace RagSystem.Tests.Ingestion;

/// <summary>These strings are written to the database, so a rename would silently break stored data.</summary>
public class StoredNamesTests
{
    [Fact]
    public void Chunk_types_have_the_agreed_names()
    {
        string[] expected =
        [
            "endpoint", "endpoint_spec", "api_overview", "workflow", "data_model",
            "faq", "enum", "errors", "glossary", "guide",
        ];

        string[] actual =
        [
            ChunkTypes.Endpoint, ChunkTypes.EndpointSpec, ChunkTypes.ApiOverview, ChunkTypes.Workflow, ChunkTypes.DataModel,
            ChunkTypes.Faq, ChunkTypes.Enum, ChunkTypes.Errors, ChunkTypes.Glossary, ChunkTypes.Guide,
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Source_types_have_the_agreed_names()
    {
        Assert.Equal("md", SourceTypes.Markdown);
        Assert.Equal("openapi", SourceTypes.OpenApi);
    }
}
