using Microsoft.AspNetCore.Mvc;

namespace RagSystem.Http;

/// <summary>
/// Error answers as <c>application/problem+json</c> with a <c>reason</c> field. This is the helper part of F03;
/// the global exception handler (and <c>correlationId</c>) come with F03 itself.
/// </summary>
public static class ProblemResults
{
    public static IActionResult Problem(int status, string reason, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["reason"] = reason;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
