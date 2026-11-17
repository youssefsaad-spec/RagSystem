using FluentValidation;
using RagSystem.Documents.Dtos;
using RagSystem.Documents.Rules;
using RagSystem.Ingestion.Rules;

namespace RagSystem.Validation.Validators;

/// <summary>
/// The rules of an upload, checked in this order. The first one that fails is the answer: a version we do not hold (400),
/// a body that is not a multipart form (400), no file or an empty file (400), a file over 10 MB (413), a file type we do
/// not read (415).
/// </summary>
public sealed class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    public UploadDocumentRequestValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(request => request.Version).MustBeAKnownVersion();

        RuleFor(request => request.IsMultipart)
            .Equal(true)
            .WithProblem(StatusCodes.Status400BadRequest, "not_multipart", "Not a form upload",
                "Send the file as multipart/form-data in the field 'file'.");

        RuleFor(request => request.FileLength)
            .GreaterThan(0)
            .WithProblem(StatusCodes.Status400BadRequest, "no_file", "No file", "The form has no file, or the file is empty.");

        RuleFor(request => request.FileLength)
            .LessThanOrEqualTo(UploadLimits.MaxBytes)
            .WithProblem(StatusCodes.Status413PayloadTooLarge, "file_too_large", "File too large",
                $"The limit is {UploadLimits.MaxBytes} bytes.");

        RuleFor(request => request.FileName)
            .Must(SupportedFileTypes.IsSupported)
            .WithProblem(StatusCodes.Status415UnsupportedMediaType, "unsupported_file_type", "Unsupported file type",
                "Use a .md file or an OpenAPI .json file.");
    }
}
