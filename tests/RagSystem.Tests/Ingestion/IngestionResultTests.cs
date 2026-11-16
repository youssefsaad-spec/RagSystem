using RagSystem.Ingestion;

namespace RagSystem.Tests.Ingestion;

public class IngestionResultTests
{
    private static IngestionResult Make(params ValidationIssue[] issues) => new()
    {
        FileName = "doc.md",
        Version = "v3",
        Title = "NDC Portal APIs V3",
        Issues = issues,
        Chunks = [new ChunkDraft(0, "text")],
    };

    private static ValidationIssue Warning() => new(IssueSeverity.Warning, "code.dropped", "A code block was dropped.", 12);

    private static ValidationIssue Error() => new(IssueSeverity.Error, "frontmatter.missing", "No header.", 1);

    [Fact]
    public void A_result_without_issues_is_valid()
    {
        Assert.True(Make().IsValid);
    }

    [Fact]
    public void Warnings_do_not_make_a_result_invalid()
    {
        var result = Make(Warning());

        Assert.True(result.IsValid);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void An_error_makes_a_result_invalid_and_errors_and_warnings_are_separated()
    {
        var result = Make(Warning(), Error(), Warning());

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("frontmatter.missing", result.Errors.Single().Code);
        Assert.Equal(2, result.Warnings.Count());
    }
}
