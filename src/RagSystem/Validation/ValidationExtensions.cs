using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using RagSystem.Documents.Rules;
using RagSystem.Http;

namespace RagSystem.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// Says how this rule is answered when it fails: <c>application/problem+json</c> with this status, this <paramref name="reason"/>
    /// (kept as the error code), this title and this detail (kept as the message).
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> WithProblem<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, int status, string reason, string title, string detail) =>
        rule.WithErrorCode(reason).WithState(_ => new ProblemInfo(status, title)).WithMessage(detail);

    /// <summary>The version must be one we hold ("v2" or "v3", any case). Failing is <c>400 unknown_version</c>.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeAKnownVersion<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(version => Versions.Normalize(version) is not null)
            .WithProblem(StatusCodes.Status400BadRequest, "unknown_version", "Unknown version",
                $"Set the 'version' query parameter to one of: {string.Join(", ", Versions.All)}.");

    /// <summary>The answer for the first failed rule. Call it only when the result is not valid.</summary>
    public static IActionResult ToProblem(this ValidationResult result)
    {
        var failure = result.Errors[0];
        var info = failure.CustomState as ProblemInfo
                   ?? new ProblemInfo(StatusCodes.Status400BadRequest, "Invalid request");
        return ProblemResults.Problem(info.Status, failure.ErrorCode, info.Title, failure.ErrorMessage);
    }
}
