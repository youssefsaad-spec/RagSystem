using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using RagSystem.Documents.Dtos;
using RagSystem.Documents.Rules;
using RagSystem.Validation;
using RagSystem.Validation.Validators;

namespace RagSystem.Tests.Validation.Validators;

public class UploadDocumentRequestValidatorTests
{
    private static readonly UploadDocumentRequestValidator Validator = new();

    private static IFormFile File(string fileName, long length = 10) =>
        new FormFile(new MemoryStream(new byte[Math.Min(length, 1024)]), 0, length, "file", fileName);

    private static ValidationResult Check(string? version = "v3", bool multipart = true, IFormFile? file = null, bool? dryRun = null) =>
        Validator.Validate(new UploadDocumentRequest(version, dryRun, multipart, file ?? File("guide.md")));

    private static void AssertFails(ValidationResult result, int status, string reason)
    {
        Assert.False(result.IsValid);
        var failure = Assert.Single(result.Errors); // the first failed rule only
        Assert.Equal(reason, failure.ErrorCode);
        Assert.Equal(status, Assert.IsType<ProblemInfo>(failure.CustomState).Status);
        Assert.False(string.IsNullOrWhiteSpace(failure.ErrorMessage));
    }

    // ---- What is accepted ----

    [Theory]
    [InlineData("v2", "guide.md")]
    [InlineData("V3", "OffersV3.swagger.json")]
    [InlineData("v3", "GUIDE.MD")]
    [InlineData("v3", "data.JSON")]
    [InlineData("v3", @"C:\somewhere\else\guide.md")] // only the name counts
    [InlineData("v3", "../../up/guide.md")]
    public void A_good_upload_has_no_failure(string version, string fileName)
    {
        Assert.True(Check(version, file: File(fileName)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void dryRun_is_not_checked(bool? dryRun)
    {
        Assert.True(Check(dryRun: dryRun).IsValid);
    }

    [Fact]
    public void A_file_of_exactly_10_MB_is_accepted()
    {
        Assert.True(Check(file: File("big.md", UploadLimits.MaxBytes)).IsValid);
    }

    // ---- Each rule ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("v4")]
    [InlineData("3")]
    [InlineData("v3 ")]
    public void A_missing_or_unknown_version_is_400_unknown_version(string? version)
    {
        AssertFails(Check(version), 400, "unknown_version");
    }

    [Fact]
    public void A_body_that_is_not_multipart_is_400_not_multipart()
    {
        AssertFails(Check(multipart: false), 400, "not_multipart");
    }

    [Fact]
    public void No_file_is_400_no_file()
    {
        var result = Validator.Validate(new UploadDocumentRequest("v3", null, true, null));

        AssertFails(result, 400, "no_file");
    }

    [Fact]
    public void An_empty_file_is_400_no_file()
    {
        AssertFails(Check(file: File("a.md", length: 0)), 400, "no_file");
    }

    [Fact]
    public void A_file_one_byte_over_10_MB_is_413_file_too_large()
    {
        AssertFails(Check(file: File("big.md", UploadLimits.MaxBytes + 1L)), 413, "file_too_large");
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("report.docx")]
    [InlineData("noextension")]
    [InlineData("guide.md.exe")]
    [InlineData("md")]
    [InlineData("folder.md/")]
    public void Any_other_file_type_is_415_unsupported_file_type(string fileName)
    {
        AssertFails(Check(file: File(fileName)), 415, "unsupported_file_type");
    }

    // ---- Order: the first failed rule is the answer ----

    [Fact]
    public void The_version_is_checked_before_anything_else()
    {
        var result = Validator.Validate(new UploadDocumentRequest("v9", null, false, null));

        AssertFails(result, 400, "unknown_version");
    }

    [Fact]
    public void A_body_that_is_not_multipart_is_reported_before_the_missing_file()
    {
        var result = Validator.Validate(new UploadDocumentRequest("v3", null, false, null));

        AssertFails(result, 400, "not_multipart");
    }

    [Fact]
    public void The_size_is_reported_before_the_file_type()
    {
        AssertFails(Check(file: File("big.txt", UploadLimits.MaxBytes + 1L)), 413, "file_too_large");
    }

    [Fact]
    public void An_empty_file_of_the_wrong_type_is_reported_as_empty_first()
    {
        AssertFails(Check(file: File("a.txt", length: 0)), 400, "no_file");
    }

    // ---- The answer made from a failure ----

    [Fact]
    public void The_failure_becomes_a_problem_json_answer_with_its_status_reason_title_and_detail()
    {
        var answer = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(Check(file: File("big.md", UploadLimits.MaxBytes + 1L)).ToProblem());

        Assert.Equal(413, answer.StatusCode);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(answer.Value);
        Assert.Equal("File too large", problem.Title);
        Assert.Equal("file_too_large", problem.Extensions["reason"]);
        Assert.Contains("10485760", problem.Detail);
        Assert.Contains("application/problem+json", answer.ContentTypes);
    }

    [Fact]
    public void A_failure_without_problem_information_is_a_plain_400_with_its_error_code()
    {
        var result = new ValidationResult([new ValidationFailure("x", "bad value") { ErrorCode = "some_code" }]);

        var answer = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result.ToProblem());

        Assert.Equal(400, answer.StatusCode);
        Assert.Equal("some_code", ((Microsoft.AspNetCore.Mvc.ProblemDetails)answer.Value!).Extensions["reason"]);
    }
}
