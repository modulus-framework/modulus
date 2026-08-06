using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class SuspendUserCommandValidator : AbstractValidator<SuspendUserCommand>
{
    public SuspendUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
