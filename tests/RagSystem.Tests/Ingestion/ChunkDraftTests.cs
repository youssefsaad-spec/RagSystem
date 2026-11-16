using RagSystem.Ingestion;

namespace RagSystem.Tests.Ingestion;

public class ChunkDraftTests
{
    private static ChunkDraft Make(string breadcrumb, string body) => new()
    {
        Ordinal = 0,
        SectionIndex = 0,
        ChunkType = ChunkTypes.Guide,
        SectionPath = ["Doc"],
        Title = "Doc",
        Entities = [],
        Extra = new Dictionary<string, object?>(),
        PartIndex = 1,
        PartCount = 1,
        Breadcrumb = breadcrumb,
        Body = body,
    };

    [Fact]
    public void Content_is_the_breadcrumb_in_brackets_then_a_new_line_then_the_body()
    {
        var chunk = Make("NDC Portal APIs V3 › FareCheck", "POST /api/V3/Order/FareCheck");

        Assert.Equal("[NDC Portal APIs V3 › FareCheck]\nPOST /api/V3/Order/FareCheck", chunk.Content);
    }

    [Fact]
    public void EstTokens_counts_the_whole_content_including_the_breadcrumb()
    {
        // "[abc]\n" is 6 characters and the body is 29, so 35 characters = exactly 10 tokens.
        var chunk = Make("abc", new string('x', 29));

        Assert.Equal(35, chunk.Content.Length);
        Assert.Equal(10, chunk.EstTokens);
    }
}
