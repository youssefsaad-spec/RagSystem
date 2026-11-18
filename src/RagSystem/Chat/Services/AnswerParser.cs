using System.Text.RegularExpressions;
using RagSystem.Chat.Models;
using RagSystem.Retrieval.Models;

namespace RagSystem.Chat.Services;

/// <summary>
/// Splits the trailing <c>Sources: S1, S3</c> line off a model reply and finds the sources it names (F23).
/// It accepts a bullet, bold marks, any case and a trailing dot. The text of the answer is returned without that line.
/// </summary>
public static partial class AnswerParser
{
    /// <summary>How many sources are cited when the model gave no usable sources line.</summary>
    public const int FallbackCitations = 3;

    [GeneratedRegex(@"^[\s>*_\-•]*sources?[\s*_]*[:：][\s*_]*(?<list>.*?)[\s*_.]*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex SourcesLine();

    [GeneratedRegex(@"(?<![A-Za-z0-9])S(?<n>\d+)(?![A-Za-z0-9])", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex Label();

    public static ParsedAnswer Parse(string reply, IReadOnlyList<RetrievedSource> sources)
    {
        var lines = reply.Replace("\r\n", "\n").Split('\n');
        var last = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (last < 0)
            return new ParsedAnswer("", [], NotCovered: false, SourcesMissing: false);

        var sourcesLine = SourcesLine().Match(lines[last]);
        if (!sourcesLine.Success)
            return new ParsedAnswer(reply.Trim(), sources.Take(FallbackCitations).ToList(), NotCovered: false, SourcesMissing: true);

        var text = string.Join("\n", lines.Take(last)).Trim();
        var list = sourcesLine.Groups["list"].Value;

        if (list.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return new ParsedAnswer(text, [], NotCovered: true, SourcesMissing: false);

        var byLabel = sources.ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);
        var cited = Label().Matches(list)
            .Select(match => "S" + match.Groups["n"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(byLabel.ContainsKey)
            .Select(label => byLabel[label])
            .ToList();

        return cited.Count > 0
            ? new ParsedAnswer(text, cited, NotCovered: false, SourcesMissing: false)
            : new ParsedAnswer(text, sources.Take(FallbackCitations).ToList(), NotCovered: false, SourcesMissing: true);
    }
}
