using FluentValidation;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Validator for VerifyEmailCommand.
/// </summary>
internal sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Verification token is required")
            .MinimumLength(32).WithMessage("Verification token must be at least 32 characters")
            .MaximumLength(128).WithMessage("Verification token cannot exceed 128 characters");
    }
}
