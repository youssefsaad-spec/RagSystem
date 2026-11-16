namespace RagSystem.Ingestion.Markdown;

/// <summary>
/// Reads the header at the top of a documentation file: <c>key: value</c> lines between two <c>---</c> lines.
/// The header is flat, so it is read here directly and no YAML package is used.
/// </summary>
public static class FrontMatterReader
{
    private const string Fence = "---";
    private static readonly string[] RequiredKeys = ["version", "title", "docType", "updated"];

    /// <param name="text">The whole file.</param>
    /// <param name="uploadVersion">The version the file is uploaded for, for example "v3". The header must agree with it.</param>
    public static FrontMatterResult Read(string text, string uploadVersion)
    {
        var lines = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n');
        var issues = new List<ValidationIssue>();

        if (lines[0].Trim() != Fence)
        {
            issues.Add(Error("frontmatter.missing",
                "The file must start with a header between two '---' lines, with version, title, docType and updated.", 1));
            return new FrontMatterResult(null, string.Join('\n', lines), 1, issues);
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == Fence);
        if (end < 0)
        {
            issues.Add(Error("frontmatter.unterminated", "The header has no closing '---' line.", 1));
            return new FrontMatterResult(null, string.Join('\n', lines), 1, issues);
        }

        var body = string.Join('\n', lines[(end + 1)..]);
        var bodyStartLine = end + 2;

        var values = ReadValues(lines, end, issues);
        if (issues.Count > 0)
            return new FrontMatterResult(null, body, bodyStartLine, issues);

        foreach (var key in RequiredKeys)
        {
            if (!values.TryGetValue(key, out var value) || value.Length == 0)
                issues.Add(Error("frontmatter.field", $"The header is missing '{key}'.", 1));
        }
        if (issues.Count > 0)
            return new FrontMatterResult(null, body, bodyStartLine, issues);

        var docType = values["docType"];
        if (!DocTypes.All.Contains(docType, StringComparer.Ordinal))
            issues.Add(Error("frontmatter.docType",
                $"docType '{docType}' is not allowed. Use one of: {string.Join(", ", DocTypes.All)}.", 1));

        var version = values["version"];
        if (!string.Equals(version, uploadVersion, StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("version.mismatch",
                $"The header version '{version}' does not match the upload version '{uploadVersion}'.", 1));

        var source = values.GetValueOrDefault("source");
        var frontMatter = new FrontMatter(
            version.ToLowerInvariant(), values["title"], docType, values["updated"],
            string.IsNullOrEmpty(source) ? null : source);
        return new FrontMatterResult(frontMatter, body, bodyStartLine, issues);
    }

    /// <summary>Reads the lines between the fences. Blank lines and lines starting with '#' are skipped.</summary>
    private static Dictionary<string, string> ReadValues(string[] lines, int end, List<ValidationIssue> issues)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < end; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                issues.Add(Error("frontmatter.invalid", $"This header line is not 'key: value': {line}", i + 1));
                continue;
            }
            values[line[..colon].Trim()] = Unquote(line[(colon + 1)..].Trim());
        }
        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            return value[1..^1];
        return value;
    }

    private static ValidationIssue Error(string code, string message, int line) =>
        new(IssueSeverity.Error, code, message, line);
}
