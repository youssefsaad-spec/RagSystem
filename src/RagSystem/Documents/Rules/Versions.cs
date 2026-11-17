namespace RagSystem.Documents.Rules;

/// <summary>The documentation versions we hold. Stored and used in lower case; a passed value may be any case.</summary>
public static class Versions
{
    public static readonly IReadOnlyList<string> All = ["v2", "v3"];

    /// <summary>Returns the version in lower case ("V3" gives "v3"), or null when it is missing or unknown.</summary>
    public static string? Normalize(string? version) =>
        All.FirstOrDefault(known => string.Equals(known, version, StringComparison.OrdinalIgnoreCase));
}
