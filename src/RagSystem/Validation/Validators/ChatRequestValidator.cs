using FluentValidation;
using RagSystem.Chat.Dtos;
using RagSystem.Retrieval.Rules;
using RagSystem.Text;

namespace RagSystem.Validation.Validators;

/// <summary>The rules of a chat message, checked in this order: a version we hold, a message that is not empty, a message that is not too long.</summary>
public sealed class ChatRequestValidator : AbstractValidator<ChatCall>
{
    public ChatRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(call => call.Version).MustBeAKnownVersion();

        RuleFor(call => call.Message)
            .Must(message => !string.IsNullOrWhiteSpace(message))
            .WithProblem(StatusCodes.Status400BadRequest, "empty_question", "Empty question", "Set 'message' in the request body to your question.");

        RuleFor(call => call.Message)
            .Must(message => TokenEstimator.Estimate(message!) <= QuestionLimits.MaxTokens)
            .WithProblem(StatusCodes.Status400BadRequest, "question_too_long", "Question too long",
                $"The limit is {QuestionLimits.MaxTokens} tokens (about {(int)(QuestionLimits.MaxTokens * 3.5)} characters).");
    }
}
