using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using RagSystem.Database.Entities;
using RagSystem.Documents.Dtos;
using RagSystem.Documents.Models;
using RagSystem.Documents.Rules;
using RagSystem.Documents.Services;
using RagSystem.Http;
using RagSystem.Validation;

namespace RagSystem.Controllers;

/// <summary>
/// Documents: upload, list, detail, chunks and delete. Every call needs <c>?version=v2|v3</c>. The rules about what a
/// request may contain are in the validators (<c>Validation\</c>); this class only runs them and calls the services.
/// Admin protection arrives with F29.
/// </summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    DocumentSubmitter submitter,
    DocumentQueries queries,
    DocumentDeleter deleter,
    IValidator<UploadDocumentRequest> uploadValidator,
    IValidator<VersionRequest> versionValidator) : ControllerBase
{
    /// <summary>
    /// Uploads a document (.md or an OpenAPI .json). With dryRun=true the file is read and cut and the report is returned
    /// with its chunks, and nothing is stored. Otherwise a valid file is queued (202) and its document is Processing until
    /// its chunks are embedded and stored. Uploading the same file name again replaces the document.
    /// </summary>
    /// <param name="version">"v2" or "v3" (any case).</param>
    /// <param name="dryRun">True to only read and cut the file.</param>
    /// <param name="file">The file, form field "file". If the form has no field of that name, its first file is used.</param>
    [HttpPost]
    [ProducesResponseType<DryRunReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<AcceptedDocument>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<DryRunReport>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upload(
        [FromQuery] string? version,
        [FromQuery] bool? dryRun,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var isMultipart = Request.HasFormContentType;
        var request = new UploadDocumentRequest(version, dryRun, isMultipart, file ?? (isMultipart ? Request.Form.Files.FirstOrDefault() : null));
        var validation = await uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return validation.ToProblem();

        using var buffer = new MemoryStream((int)request.FileLength);
        await request.File!.CopyToAsync(buffer, cancellationToken);

        var knownVersion = Versions.Normalize(version)!;
        var outcome = await submitter.SubmitAsync(knownVersion, request.FileName, buffer.ToArray(), dryRun ?? false, cancellationToken);
        return outcome switch
        {
            SubmitReport { DryRun: true } report => Ok(DryRunReport.From(report.Result)),
            SubmitReport report => UnprocessableEntity(DryRunReport.From(report.Result) with { Chunks = [] }),
            SubmitAccepted accepted => Accepted($"/api/documents/{accepted.Id}?version={knownVersion}",
                new AcceptedDocument(accepted.Id, nameof(DocumentStatus.Processing))),
            SubmitBusy busy => ProblemResults.Problem(StatusCodes.Status409Conflict, "document_processing", "Already processing",
                $"Document {busy.Id} is still being processed. Wait until it is Ready or Failed, then upload again."),
            _ => throw new InvalidOperationException("Unknown upload outcome."),
        };
    }

    /// <summary>The documents of a version, by file name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] string? version, CancellationToken cancellationToken)
    {
        if (await InvalidVersionAsync(version, cancellationToken) is { } invalid)
            return invalid;

        return Ok(await queries.ListAsync(Versions.Normalize(version)!, cancellationToken));
    }

    /// <summary>One document: its status and chunk count.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] string? version, CancellationToken cancellationToken)
    {
        if (await InvalidVersionAsync(version, cancellationToken) is { } invalid)
            return invalid;

        return await queries.GetAsync(Versions.Normalize(version)!, id, cancellationToken) is { } document
            ? Ok(document)
            : DocumentNotFound(id);
    }

    /// <summary>The stored chunks of a document, in order, without their vectors.</summary>
    /// <param name="skip">How many chunks to skip. Default 0; a negative number counts as 0.</param>
    /// <param name="take">How many to return. Default 100; below 1 counts as 1, above 500 as 500.</param>
    [HttpGet("{id:guid}/chunks")]
    [ProducesResponseType<IReadOnlyList<ChunkView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Chunks(
        Guid id, [FromQuery] string? version, [FromQuery] int? skip, [FromQuery] int? take, CancellationToken cancellationToken)
    {
        if (await InvalidVersionAsync(version, cancellationToken) is { } invalid)
            return invalid;

        return await queries.ChunksAsync(Versions.Normalize(version)!, id, skip, take, cancellationToken) is { } chunks
            ? Ok(chunks)
            : DocumentNotFound(id);
    }

    /// <summary>Deletes a document and its chunks.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? version, CancellationToken cancellationToken)
    {
        if (await InvalidVersionAsync(version, cancellationToken) is { } invalid)
            return invalid;

        return await deleter.DeleteAsync(Versions.Normalize(version)!, id, cancellationToken) ? NoContent() : DocumentNotFound(id);
    }

    /// <returns>The 400 answer when the version is not valid, otherwise null.</returns>
    private async Task<IActionResult?> InvalidVersionAsync(string? version, CancellationToken cancellationToken)
    {
        var validation = await versionValidator.ValidateAsync(new VersionRequest(version), cancellationToken);
        return validation.IsValid ? null : validation.ToProblem();
    }

    private static IActionResult DocumentNotFound(Guid id) =>
        ProblemResults.Problem(StatusCodes.Status404NotFound, "document_not_found", "Document not found",
            $"There is no document {id} in this version.");
}
