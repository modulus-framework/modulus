using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Permissions.Commands;

internal sealed class RemovePermissionCommandValidator : AbstractValidator<RemovePermissionCommand>
{
    public RemovePermissionCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Permission)
            .NotEmpty()
            .MaximumLength(200);
    }
}
