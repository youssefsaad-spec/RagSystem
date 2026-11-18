using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RagSystem.Chat.Dtos;
using RagSystem.Chat.Exceptions;
using RagSystem.Chat.Services;
using RagSystem.Documents.Rules;
using RagSystem.Embeddings.Exceptions;
using RagSystem.Http;
using RagSystem.Validation;

namespace RagSystem.Controllers;

/// <summary>Asks a question about the documentation of one version and gets an answer with its sources. Open until F29.</summary>
[ApiController]
[Route("api/{version}/chat")]
public sealed class ChatController(ChatPipeline pipeline, IValidator<ChatCall> validator, ILogger<ChatController> logger) : ControllerBase
{
    /// <param name="version">"v2" or "v3" (any case).</param>
    /// <param name="request">The question, and the conversation to continue (leave it out to start a new one).</param>
    [HttpPost]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Chat(string version, [FromBody] ChatRequest? request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(new ChatCall(version, request?.Message), cancellationToken);
        if (!validation.IsValid)
            return validation.ToProblem();

        try
        {
            var result = await pipeline.AskAsync(Versions.Normalize(version)!, request!.Message!, request.ConversationId, clientId: null, cancellationToken);
            return Ok(ChatResponse.From(result));
        }
        catch (ConversationNotFoundException)
        {
            return ConversationProblems.NotFound();
        }
        catch (EmbeddingException error)
        {
            // The answer to the caller hides the cause on purpose; the log keeps it (no question or answer text in it).
            logger.LogWarning(error, "chat: the model call failed ({Kind}): {Reason}", error.Kind, error.Message);
            return ModelProblems.For(error, Response);
        }
        catch (EmptyAnswerException)
        {
            return ModelProblems.EmptyAnswer();
        }
    }
}
