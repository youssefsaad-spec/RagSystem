using Microsoft.AspNetCore.Mvc;
using RagSystem.Documents;
using RagSystem.Http;
using RagSystem.Ingestion;

namespace RagSystem.Controllers;

/// <summary>
/// Uploading documents. Only the dry run exists so far (F09): the file is read and cut and the report is returned,
/// nothing is stored. Storing arrives with F14 and F16. Admin protection arrives with F29.
/// </summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(DocumentProcessor processor) : ControllerBase
{
    /// <summary>
    /// Reads and cuts the uploaded file and returns the report with its chunks. Nothing is stored.
    /// </summary>
    /// <param name="version">"v2" or "v3" (any case).</param>
    /// <param name="dryRun">Must be true for now: storing arrives later.</param>
    /// <param name="file">The file, form field "file". If the form has no field of that name, its first file is used.</param>
    [HttpPost]
    [ProducesResponseType<DryRunReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented)]
    public async Task<IActionResult> Upload(
        [FromQuery] string? version,
        [FromQuery] bool? dryRun,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        version = Versions.Normalize(version);
        if (version is null)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "unknown_version", "Unknown version",
                $"The version must be one of: {string.Join(", ", Versions.All)}.");

        if (dryRun != true)
            return ProblemResults.Problem(StatusCodes.Status501NotImplemented, "not_implemented", "Not implemented",
                "Only a dry run exists so far. Add dryRun=true.");

        if (!Request.HasFormContentType)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "not_multipart", "Not a form upload",
                "Send the file as multipart/form-data in the field 'file'.");

        file ??= Request.Form.Files.FirstOrDefault();
        if (file is null)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "no_file", "No file", "The form has no file.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        try
        {
            // Never trust a path from the client: keep only the name.
            var result = processor.Process(Path.GetFileName(file.FileName), buffer.ToArray(), version);
            return Ok(DryRunReport.From(result));
        }
        catch (UnsupportedFileTypeException error)
        {
            return ProblemResults.Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_file_type",
                "Unsupported file type", error.Message);
        }
    }
}
