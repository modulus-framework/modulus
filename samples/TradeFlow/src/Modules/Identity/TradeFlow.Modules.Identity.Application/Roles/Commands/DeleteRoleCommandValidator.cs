using FluentValidation;

namespace TradeFlow.Modules.Identity.Application.Roles.Commands;

internal sealed class DeleteRoleCommandValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
    }
}
