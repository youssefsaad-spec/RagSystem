using RagSystem.Retrieval.Dtos;
using RagSystem.Retrieval.Rules;
using RagSystem.Validation;
using RagSystem.Validation.Validators;

namespace RagSystem.Tests.Validation.Validators;

public class SearchRequestValidatorTests
{
    private static readonly SearchRequestValidator Validator = new();

    private static (string Reason, int Status)? FirstFailure(string? version, string? question)
    {
        var result = Validator.Validate(new SearchRequest(version, question));
        if (result.IsValid)
            return null;
        var failure = result.Errors[0];
        return (failure.ErrorCode, ((ProblemInfo)failure.CustomState).Status);
    }

    [Theory]
    [InlineData("v3", "What does error 409 mean?")]
    [InlineData("V2", "FareCheck")]
    public void A_known_version_and_a_question_are_valid(string version, string question)
    {
        Assert.Null(FirstFailure(version, question));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v4")]
    public void An_unknown_version_is_400_unknown_version(string? version)
    {
        Assert.Equal(("unknown_version", 400), FirstFailure(version, "a question"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_question_is_400_empty_question(string? question)
    {
        Assert.Equal(("empty_question", 400), FirstFailure("v3", question));
    }

    [Fact]
    public void A_question_over_the_token_limit_is_400_question_too_long_and_one_at_the_limit_is_valid()
    {
        var atLimit = new string('a', (int)(QuestionLimits.MaxTokens * 3.5));
        var overLimit = atLimit + "aaaa";

        Assert.Null(FirstFailure("v3", atLimit));
        Assert.Equal(("question_too_long", 400), FirstFailure("v3", overLimit));
    }

    [Fact]
    public void The_version_is_checked_before_the_question()
    {
        Assert.Equal(("unknown_version", 400), FirstFailure("v9", ""));
    }
}
