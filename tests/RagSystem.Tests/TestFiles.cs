namespace RagSystem.Tests;

/// <summary>Where the real input files live (the old RagDocs folder, read only).</summary>
public static class TestFiles
{
    public const string Root = @"D:\RAG\DotNet";

    public static string Path(string relativePath)
    {
        var full = System.IO.Path.Combine(Root, relativePath);
        Assert.True(File.Exists(full), $"The input file is missing: {full}");
        return full;
    }

    public static string ReadAllText(string relativePath) => File.ReadAllText(Path(relativePath));
}
