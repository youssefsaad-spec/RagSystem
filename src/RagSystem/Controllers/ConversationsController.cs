using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RagSystem.Chat.Dtos;
using RagSystem.Chat.Exceptions;
using RagSystem.Chat.Services;
using RagSystem.Documents.Dtos;
using RagSystem.Documents.Rules;
using RagSystem.Http;
using RagSystem.Validation;

namespace RagSystem.Controllers;

/// <summary>Reads stored conversations. Open until F29, when a client sees only its own.</summary>
[ApiController]
[Route("api/{version}/conversations")]
public sealed class ConversationsController(ConversationStore store, IValidator<VersionRequest> validator) : ControllerBase
{
    /// <summary>The messages of a conversation, oldest first, with the citations and token counts of the answers.</summary>
    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType<IReadOnlyList<MessageView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Messages(string version, Guid id, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(new VersionRequest(version), cancellationToken);
        if (!validation.IsValid)
            return validation.ToProblem();

        try
        {
            return Ok(await store.GetMessagesAsync(id, Versions.Normalize(version)!, clientId: null, cancellationToken));
        }
        catch (ConversationNotFoundException)
        {
            return ConversationProblems.NotFound();
        }
    }
}
