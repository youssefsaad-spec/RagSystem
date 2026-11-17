using RagSystem.Ingestion.Rules;

namespace RagSystem.Tests.Ingestion.Rules;

public class SupportedFileTypesTests
{
    [Theory]
    [InlineData("a.md", true)]
    [InlineData("A.MD", true)]
    [InlineData("swagger.json", true)]
    [InlineData("swagger.JSON", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.md.bak", false)]
    [InlineData("md", false)]
    [InlineData("", false)]
    public void Only_md_and_json_files_are_supported(string fileName, bool supported)
    {
        Assert.Equal(supported, SupportedFileTypes.IsSupported(fileName));
        Assert.Equal(supported && fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase), SupportedFileTypes.IsMarkdown(fileName));
        Assert.Equal(supported && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase), SupportedFileTypes.IsOpenApi(fileName));
    }
}
