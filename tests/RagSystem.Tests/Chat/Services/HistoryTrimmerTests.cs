using RagSystem.Chat.Models;
using RagSystem.Chat.Services;

namespace RagSystem.Tests.Chat.Services;

public class HistoryTrimmerTests
{
    private static HistoryTurn Turn(string name, int tokens) => new($"Q {name}", $"A {name}", tokens);

    [Fact]
    public void The_newest_pairs_are_kept_and_the_result_is_oldest_first()
    {
        var newestFirst = new[] { Turn("3", 10), Turn("2", 10), Turn("1", 10) };

        var result = HistoryTrimmer.Trim(newestFirst, maxTurns: 6, maxTokens: 2000);

        Assert.Equal(["Q 1", "Q 2", "Q 3"], result.Select(t => t.Question));
    }

    [Fact]
    public void At_most_the_given_number_of_turns_are_kept_the_newest_ones()
    {
        var newestFirst = Enumerable.Range(1, 10).Reverse().Select(n => Turn(n.ToString(), 1)).ToList();

        var result = HistoryTrimmer.Trim(newestFirst, maxTurns: 6, maxTokens: 2000);

        Assert.Equal(6, result.Count);
        Assert.Equal("Q 5", result[0].Question);
        Assert.Equal("Q 10", result[^1].Question);
    }

    [Fact]
    public void It_stops_at_the_first_pair_that_does_not_fit_even_if_an_older_smaller_one_would()
    {
        // Newest first: 900, 1500 (does not fit: 2400 > 2000), 10 (would fit but must not be taken: no gaps).
        var newestFirst = new[] { Turn("3", 900), Turn("2", 1500), Turn("1", 10) };

        var result = HistoryTrimmer.Trim(newestFirst, maxTurns: 6, maxTokens: 2000);

        Assert.Equal(["Q 3"], result.Select(t => t.Question));
    }

    [Fact]
    public void A_pair_that_alone_is_over_the_limit_gives_no_history()
    {
        Assert.Empty(HistoryTrimmer.Trim([Turn("1", 2001)], maxTurns: 6, maxTokens: 2000));
    }

    [Fact]
    public void A_pair_that_exactly_fills_the_limit_fits()
    {
        Assert.Single(HistoryTrimmer.Trim([Turn("1", 2000)], maxTurns: 6, maxTokens: 2000));
    }

    [Fact]
    public void No_history_gives_an_empty_list()
    {
        Assert.Empty(HistoryTrimmer.Trim([], maxTurns: 6, maxTokens: 2000));
    }
}
