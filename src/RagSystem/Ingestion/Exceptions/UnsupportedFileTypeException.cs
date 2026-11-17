namespace RagSystem.Ingestion.Exceptions;

/// <summary>The file is not a type we read. The upload endpoint answers it with 415.</summary>
public sealed class UnsupportedFileTypeException(string fileName)
    : Exception($"'{fileName}' is not a supported file type. Use .md or .json.")
{
    public string FileName { get; } = fileName;
}
