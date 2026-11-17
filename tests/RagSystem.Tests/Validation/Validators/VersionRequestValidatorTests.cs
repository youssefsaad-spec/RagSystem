using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RagSystem.Documents.Dtos;
using RagSystem.Validation;
using RagSystem.Validation.Validators;

namespace RagSystem.Tests.Validation.Validators;

public class VersionRequestValidatorTests(TestApp factory) : IClassFixture<TestApp>
{
    private static readonly VersionRequestValidator Validator = new();

    [Theory]
    [InlineData("v2")]
    [InlineData("v3")]
    [InlineData("V2")]
    [InlineData("V3")]
    public void A_known_version_in_any_case_is_valid(string version)
    {
        Assert.True(Validator.Validate(new VersionRequest(version)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("v1")]
    [InlineData("v4")]
    [InlineData("v3,v2")]
    [InlineData("3")]
    public void Anything_else_is_400_unknown_version_and_the_message_names_the_versions_we_hold(string? version)
    {
        var result = Validator.Validate(new VersionRequest(version));

        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Errors);
        Assert.Equal("unknown_version", failure.ErrorCode);
        Assert.Equal(400, Assert.IsType<ProblemInfo>(failure.CustomState).Status);
        Assert.Contains("v2, v3", failure.ErrorMessage);
    }

    [Fact]
    public void The_app_has_every_validator_registered()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<VersionRequestValidator>(scope.ServiceProvider.GetRequiredService<IValidator<VersionRequest>>());
        Assert.IsType<UploadDocumentRequestValidator>(scope.ServiceProvider.GetRequiredService<IValidator<UploadDocumentRequest>>());
    }
}
