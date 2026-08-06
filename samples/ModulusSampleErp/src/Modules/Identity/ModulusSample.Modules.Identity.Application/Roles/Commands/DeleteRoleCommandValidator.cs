using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Roles.Commands;

internal sealed class DeleteRoleCommandValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
    }
}
