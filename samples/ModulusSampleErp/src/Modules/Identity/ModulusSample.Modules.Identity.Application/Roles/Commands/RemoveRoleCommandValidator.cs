using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

internal sealed class RemoveRoleCommandValidator : AbstractValidator<RemoveRoleCommand>
{
    public RemoveRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}
