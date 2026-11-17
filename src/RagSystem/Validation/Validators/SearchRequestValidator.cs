using FluentValidation;
using RagSystem.Retrieval.Dtos;
using RagSystem.Retrieval.Rules;
using RagSystem.Text;

namespace RagSystem.Validation.Validators;

/// <summary>The rules of a search, checked in this order: a version we hold, a question that is not empty, a question that is not too long.</summary>
public sealed class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    public SearchRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(request => request.Version).MustBeAKnownVersion();

        RuleFor(request => request.Question)
            .Must(question => !string.IsNullOrWhiteSpace(question))
            .WithProblem(StatusCodes.Status400BadRequest, "empty_question", "Empty question", "Set the 'q' query parameter to your question.");

        RuleFor(request => request.Question)
            .Must(question => TokenEstimator.Estimate(question!) <= QuestionLimits.MaxTokens)
            .WithProblem(StatusCodes.Status400BadRequest, "question_too_long", "Question too long",
                $"The limit is {QuestionLimits.MaxTokens} tokens (about {(int)(QuestionLimits.MaxTokens * 3.5)} characters).");
    }
}
