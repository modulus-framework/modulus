using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
