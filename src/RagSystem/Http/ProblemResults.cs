namespace RagSystem.Http;

/// <summary>
/// Error answers as <c>application/problem+json</c> with a <c>reason</c> field. This is the helper part of F03;
/// the global exception handler (and <c>correlationId</c>) come with F03 itself.
/// </summary>
public static class ProblemResults
{
    public static IResult Problem(int status, string reason, string title, string detail) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["reason"] = reason });
}
