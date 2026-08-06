using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Validator for ResendEmailVerificationCommand.
/// </summary>
internal sealed class ResendEmailVerificationCommandValidator : AbstractValidator<ResendEmailVerificationCommand>
{
    public ResendEmailVerificationCommandValidator()
    {
        // No validation needed for empty command
        // User context will be validated in the handler
    }
}
