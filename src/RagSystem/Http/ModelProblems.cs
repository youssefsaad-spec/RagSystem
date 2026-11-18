using Microsoft.AspNetCore.Mvc;
using RagSystem.Embeddings.Exceptions;

namespace RagSystem.Http;

/// <summary>
/// Turns a failed model call into an answer: the model service down, loading, busy or too slow is <c>503 llm_unavailable</c> with a
/// <c>Retry-After</c>; a missing model or a rejected key or another error that will not go away by waiting is <c>502 llm_error</c>.
/// This is F24: it maps the failures of the embedding calls and of the chat calls (a busy gate and a timeout are transient too).
/// </summary>
public static class ModelProblems
{
    public const int RetryAfterSeconds = 30;

    public static IActionResult For(EmbeddingException error, HttpResponse response)
    {
        if (error.Kind == EmbeddingFailureKind.Fatal)
            return ProblemResults.Problem(StatusCodes.Status502BadGateway, "llm_error", "Model error",
                $"The model service refused the request: {error.Message}");

        var seconds = error.RetryAfter is { } wait ? (int)Math.Ceiling(wait.TotalSeconds) : RetryAfterSeconds;
        response.Headers.RetryAfter = seconds.ToString();
        return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, "llm_unavailable", "Model service unavailable",
            "The model service is not ready or is busy. Try again shortly.");
    }

    /// <summary>The model answered, but with no text (for example the reply was blocked). <c>502 empty_answer</c>.</summary>
    public static IActionResult EmptyAnswer() =>
        ProblemResults.Problem(StatusCodes.Status502BadGateway, "empty_answer", "Empty answer",
            "The model returned no answer. Try again, or rephrase the question.");
}
