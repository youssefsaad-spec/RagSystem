using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RagSystem.Documents.Rules;
using RagSystem.Http;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Retrieval.Dtos;
using RagSystem.Retrieval.Services;
using RagSystem.Validation;

namespace RagSystem.Controllers;

/// <summary>
/// Shows what retrieval finds for a question, without calling a chat model: the chunks that would be sent to it and the
/// candidates that were dropped, with scores. For checking and tuning (F28). Open until F29.
/// </summary>
[ApiController]
[Route("api/{version}/search")]
public sealed class SearchController(Retriever retriever, IValidator<SearchRequest> validator) : ControllerBase
{
    /// <param name="version">"v2" or "v3" (any case).</param>
    /// <param name="q">The question.</param>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search(string version, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(new SearchRequest(version, q), cancellationToken);
        if (!validation.IsValid)
            return validation.ToProblem();

        var knownVersion = Versions.Normalize(version)!;
        try
        {
            var result = await retriever.RetrieveAsync(knownVersion, q!, cancellationToken);
            return Ok(SearchResponse.From(knownVersion, q!, result));
        }
        catch (EmbeddingException error)
        {
            return ModelProblems.For(error, Response);
        }
    }
}
