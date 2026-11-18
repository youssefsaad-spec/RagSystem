using RagSystem.Chat.Dtos;
using RagSystem.Validation;
using RagSystem.Validation.Validators;

namespace RagSystem.Tests.Validation.Validators;

public class ChatRequestValidatorTests
{
    private static readonly ChatRequestValidator Validator = new();

    private static (string Reason, ProblemInfo Info) FirstFailure(ChatCall call)
    {
        var failure = Validator.Validate(call).Errors[0];
        return (failure.ErrorCode, (ProblemInfo)failure.CustomState);
    }

    [Theory]
    [InlineData("v3")]
    [InlineData("V2")]
    public void A_known_version_and_a_message_is_valid(string version)
    {
        Assert.True(Validator.Validate(new ChatCall(version, "What does FareCheck do?")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("v4")]
    [InlineData("")]
    public void An_unknown_version_is_400_unknown_version(string? version)
    {
        var (reason, info) = FirstFailure(new ChatCall(version, "hello"));

        Assert.Equal("unknown_version", reason);
        Assert.Equal(400, info.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    public void A_missing_or_blank_message_is_400_empty_question(string? message)
    {
        var (reason, info) = FirstFailure(new ChatCall("v3", message));

        Assert.Equal("empty_question", reason);
        Assert.Equal(400, info.Status);
    }

    [Fact]
    public void A_message_over_1000_estimated_tokens_is_400_question_too_long_and_exactly_1000_is_fine()
    {
        Assert.True(Validator.Validate(new ChatCall("v3", new string('a', 3500))).IsValid); // 1,000 tokens

        var (reason, info) = FirstFailure(new ChatCall("v3", new string('a', 3501)));

        Assert.Equal("question_too_long", reason);
        Assert.Equal(400, info.Status);
    }

    [Fact]
    public void The_version_is_checked_before_the_message()
    {
        Assert.Equal("unknown_version", FirstFailure(new ChatCall("v9", null)).Reason);
    }
}
