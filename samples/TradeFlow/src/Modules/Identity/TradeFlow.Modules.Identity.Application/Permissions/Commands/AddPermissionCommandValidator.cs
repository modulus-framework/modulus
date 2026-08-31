using FluentValidation;

namespace TradeFlow.Modules.Identity.Application.Permissions.Commands;

internal sealed class AddPermissionCommandValidator : AbstractValidator<AddPermissionCommand>
{
    public AddPermissionCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Permission)
            .NotEmpty()
            .MaximumLength(200);
    }
}
