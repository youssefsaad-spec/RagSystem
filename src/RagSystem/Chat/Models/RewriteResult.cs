namespace RagSystem.Chat.Models;

/// <summary>The question used for retrieval (F27).</summary>
/// <param name="Query">The text to search with.</param>
/// <param name="IsStandalone">True when <paramref name="Query"/> can be read without the conversation (the question itself, or a rewrite). False for the fallback.</param>
/// <param name="FallbackReason">Why the rewrite was not used (see <c>RewriteReasons</c>). Null when it was used or not needed.</param>
public sealed record RewriteResult(string Query, bool IsStandalone, string? FallbackReason);
