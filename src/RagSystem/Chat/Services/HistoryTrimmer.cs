using RagSystem.Chat.Models;

namespace RagSystem.Chat.Services;

/// <summary>Chooses which earlier turns go into the prompt (F26).</summary>
public static class HistoryTrimmer
{
    /// <summary>
    /// Takes whole question-and-answer pairs, newest first, until <paramref name="maxTurns"/> pairs or <paramref name="maxTokens"/>
    /// tokens. It stops at the first pair that does not fit, so there are no gaps. The result is oldest first.
    /// </summary>
    /// <param name="newestFirst">All earlier pairs, newest first.</param>
    public static IReadOnlyList<HistoryTurn> Trim(IEnumerable<HistoryTurn> newestFirst, int maxTurns, int maxTokens)
    {
        var chosen = new List<HistoryTurn>();
        var tokens = 0;
        foreach (var turn in newestFirst)
        {
            if (chosen.Count >= maxTurns || tokens + turn.EstTokens > maxTokens)
                break;
            chosen.Add(turn);
            tokens += turn.EstTokens;
        }

        chosen.Reverse();
        return chosen;
    }
}
