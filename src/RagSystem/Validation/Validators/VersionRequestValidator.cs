using FluentValidation;
using RagSystem.Documents.Dtos;

namespace RagSystem.Validation.Validators;

/// <summary>The rules of a call that only needs a version.</summary>
public sealed class VersionRequestValidator : AbstractValidator<VersionRequest>
{
    public VersionRequestValidator()
    {
        RuleFor(request => request.Version).MustBeAKnownVersion();
    }
}
