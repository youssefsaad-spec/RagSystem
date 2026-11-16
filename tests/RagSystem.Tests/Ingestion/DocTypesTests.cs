using RagSystem.Ingestion;

namespace RagSystem.Tests.Ingestion;

public class DocTypesTests
{
    [Fact]
    public void The_accepted_doc_types_are_guide_and_whats_new()
    {
        Assert.Equal(["guide", "whats-new"], DocTypes.All);
    }
}
