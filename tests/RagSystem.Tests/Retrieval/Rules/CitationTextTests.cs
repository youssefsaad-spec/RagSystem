using RagSystem.Retrieval.Rules;

namespace RagSystem.Tests.Retrieval.Rules;

public class CitationTextTests
{
    [Theory]
    [InlineData("NDC Portal APIs V3", 0, "NDC Portal APIs V3, part 1")]
    [InlineData("NDC Portal APIs V3", 11, "NDC Portal APIs V3, part 12")]
    public void The_part_number_is_the_stored_position_plus_1(string title, int ordinal, string expected)
    {
        Assert.Equal(expected, CitationText.Of(title, ordinal));
    }
}
