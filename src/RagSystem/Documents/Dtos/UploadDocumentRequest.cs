namespace RagSystem.Documents.Dtos;

/// <summary>What an upload sends. Checked by <c>UploadDocumentRequestValidator</c>.</summary>
/// <param name="Version">The <c>version</c> query parameter, as the caller wrote it.</param>
/// <param name="DryRun">The <c>dryRun</c> query parameter.</param>
/// <param name="IsMultipart">Whether the request body is a multipart form.</param>
/// <param name="File">The uploaded file: the form field "file", or else the form's first file.</param>
public sealed record UploadDocumentRequest(string? Version, bool? DryRun, bool IsMultipart, IFormFile? File)
{
    /// <summary>The size of the file in bytes, or 0 when there is none.</summary>
    public long FileLength => File?.Length ?? 0;

    /// <summary>Only the name, never a path the client put in front of it. Empty when there is no file.</summary>
    public string FileName => Path.GetFileName(File?.FileName ?? "");
}
