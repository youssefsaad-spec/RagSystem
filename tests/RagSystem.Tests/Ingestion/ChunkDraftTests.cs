using RagSystem.Ingestion;

namespace RagSystem.Tests.Ingestion;

public class ChunkDraftTests
{
    [Fact]
    public void A_chunk_keeps_its_position_and_its_text_exactly()
    {
        var chunk = new ChunkDraft(7, "| Field | Description |\n|---|---|\n| `a` | one<br>two |");

        Assert.Equal(7, chunk.Ordinal);
        Assert.Equal("| Field | Description |\n|---|---|\n| `a` | one<br>two |", chunk.Text);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(35, 10)]  // exactly 10.0 tokens
    [InlineData(36, 11)]  // 10.29 rounds up
    public void EstTokens_is_the_token_estimate_of_the_text(int length, int expected)
    {
        Assert.Equal(expected, new ChunkDraft(0, new string('x', length)).EstTokens);
    }
}
