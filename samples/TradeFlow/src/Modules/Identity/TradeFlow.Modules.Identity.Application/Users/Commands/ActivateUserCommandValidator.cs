using FluentValidation;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

internal sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
