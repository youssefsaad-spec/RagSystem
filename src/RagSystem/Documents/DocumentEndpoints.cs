using RagSystem.Http;
using RagSystem.Ingestion;

namespace RagSystem.Documents;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents", Upload);
    }

    /// <summary>
    /// Reads and cuts the uploaded file and answers with the report and the chunks. Only the dry run exists so far:
    /// storing arrives with F14 and F16. Admin protection arrives with F29.
    /// </summary>
    private static async Task<IResult> Upload(HttpRequest request, DocumentProcessor processor, string? version, bool? dryRun)
    {
        version = Versions.Normalize(version);
        if (version is null)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "unknown_version", "Unknown version",
                $"The version must be one of: {string.Join(", ", Versions.All)}.");

        if (dryRun != true)
            return ProblemResults.Problem(StatusCodes.Status501NotImplemented, "not_implemented", "Not implemented",
                "Only a dry run exists so far. Add dryRun=true.");

        if (!request.HasFormContentType)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "not_multipart", "Not a form upload",
                "Send the file as multipart/form-data in the field 'file'.");

        IFormFileCollection files;
        try
        {
            files = (await request.ReadFormAsync(request.HttpContext.RequestAborted)).Files;
        }
        catch (InvalidDataException)
        {
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "not_multipart", "Not a form upload",
                "The multipart form could not be read.");
        }

        var file = files.GetFile("file") ?? files.FirstOrDefault();
        if (file is null)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest, "no_file", "No file", "The form has no file.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, request.HttpContext.RequestAborted);

        try
        {
            // Never trust a path from the client: keep only the name.
            var result = processor.Process(Path.GetFileName(file.FileName), buffer.ToArray(), version);
            return Results.Ok(DryRunReport.From(result));
        }
        catch (UnsupportedFileTypeException error)
        {
            return ProblemResults.Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_file_type",
                "Unsupported file type", error.Message);
        }
    }
}
