using RagSystem.Chat.Services;
using RagSystem.Retrieval.Models;

namespace RagSystem.Tests.Chat.Services;

public class AnswerParserTests
{
    private static RetrievedSource Source(int n) =>
        new($"S{n}", "NDC Portal APIs V3", n + 10, $"text {n}", 10, Guid.NewGuid(), 0.9);

    private static readonly IReadOnlyList<RetrievedSource> Sources = [Source(1), Source(2), Source(3), Source(4), Source(5)];

    [Fact]
    public void The_sources_line_is_taken_off_and_the_named_sources_are_cited_in_the_order_given()
    {
        var parsed = AnswerParser.Parse("FareCheck checks the fare.\n\nSources: S3, S1", Sources);

        Assert.Equal("FareCheck checks the fare.", parsed.Text);
        Assert.Equal(["S3", "S1"], parsed.Cited.Select(s => s.Label));
        Assert.False(parsed.SourcesMissing);
        Assert.False(parsed.NotCovered);
    }

    [Theory]
    [InlineData("Sources: S1, S2")]
    [InlineData("sources: s1, s2")]
    [InlineData("SOURCES: S1 and S2.")]
    [InlineData("**Sources:** S1, S2")]
    [InlineData("**Sources**: S1, S2")]
    [InlineData("- Sources: S1, S2")]
    [InlineData("* Sources: S1; S2")]
    [InlineData("> Sources: [S1], [S2]")]
    [InlineData("Source: S1, S2")]
    [InlineData("Sources: S1, S2.  ")]
    [InlineData("_Sources: S1, S2_")]
    public void The_sources_line_is_found_with_bullets_bold_case_and_a_trailing_dot(string line)
    {
        var parsed = AnswerParser.Parse("The answer.\n" + line, Sources);

        Assert.Equal("The answer.", parsed.Text);
        Assert.Equal(["S1", "S2"], parsed.Cited.Select(s => s.Label));
        Assert.False(parsed.SourcesMissing);
    }

    [Fact]
    public void Blank_lines_after_the_sources_line_and_windows_line_ends_are_fine()
    {
        var parsed = AnswerParser.Parse("Line one.\r\nLine two.\r\nSources: S2\r\n\r\n", Sources);

        Assert.Equal("Line one.\nLine two.", parsed.Text);
        Assert.Equal(["S2"], parsed.Cited.Select(s => s.Label));
    }

    [Fact]
    public void A_source_named_twice_is_cited_once_and_an_unknown_label_is_ignored()
    {
        var parsed = AnswerParser.Parse("Answer.\nSources: S2, S9, S2", Sources);

        Assert.Equal(["S2"], parsed.Cited.Select(s => s.Label));
        Assert.False(parsed.SourcesMissing);
    }

    [Theory]
    [InlineData("Sources: none")]
    [InlineData("Sources: None.")]
    [InlineData("**Sources:** none")]
    public void Sources_none_means_the_documentation_does_not_cover_it_and_nothing_is_cited(string line)
    {
        var parsed = AnswerParser.Parse("I could not find that in the documentation.\n" + line, Sources);

        Assert.True(parsed.NotCovered);
        Assert.Empty(parsed.Cited);
        Assert.False(parsed.SourcesMissing);
        Assert.Equal("I could not find that in the documentation.", parsed.Text);
    }

    [Fact]
    public void A_reply_with_no_sources_line_cites_the_first_three_and_says_the_line_was_missing()
    {
        var parsed = AnswerParser.Parse("An answer with no sources line.", Sources);

        Assert.True(parsed.SourcesMissing);
        Assert.Equal(["S1", "S2", "S3"], parsed.Cited.Select(s => s.Label));
        Assert.Equal("An answer with no sources line.", parsed.Text);
    }

    [Fact]
    public void A_sources_line_with_no_valid_label_counts_as_missing_but_is_still_taken_off()
    {
        var parsed = AnswerParser.Parse("An answer.\nSources: the guide", Sources);

        Assert.True(parsed.SourcesMissing);
        Assert.Equal(3, parsed.Cited.Count);
        Assert.Equal("An answer.", parsed.Text);
    }

    [Fact]
    public void Fewer_than_three_sources_are_all_cited_when_the_line_is_missing()
    {
        var parsed = AnswerParser.Parse("An answer.", [Source(1), Source(2)]);

        Assert.Equal(2, parsed.Cited.Count);
    }

    [Fact]
    public void The_word_sources_in_the_middle_of_the_text_is_not_a_sources_line()
    {
        var parsed = AnswerParser.Parse("Sources: S1 is mentioned here.\nMore text after it.", Sources);

        Assert.True(parsed.SourcesMissing); // the last line is not a sources line
        Assert.Contains("More text after it.", parsed.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \n")]
    [InlineData("Sources: S1")]
    [InlineData("\n\nSources: none\n")]
    public void Nothing_left_after_the_sources_line_is_an_empty_answer(string reply)
    {
        var parsed = AnswerParser.Parse(reply, Sources);

        Assert.True(parsed.IsEmpty);
    }

    [Fact]
    public void A_sources_line_whose_label_has_extra_letters_does_not_count_as_that_label()
    {
        var parsed = AnswerParser.Parse("Answer.\nSources: S1x, XS2", Sources);

        Assert.True(parsed.SourcesMissing);
    }
}
