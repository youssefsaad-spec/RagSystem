using Microsoft.AspNetCore.Mvc;

namespace RagSystem.Http;

/// <summary>The answer for a conversation that cannot be used. A conversation of another client or version looks exactly like one that does not exist.</summary>
public static class ConversationProblems
{
    public static IActionResult NotFound() =>
        ProblemResults.Problem(StatusCodes.Status404NotFound, "conversation_not_found", "Conversation not found",
            "There is no conversation with this id for this version.");
}
