using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Roles.Queries;

internal sealed class GetRoleByIdQueryValidator : AbstractValidator<GetRoleByIdQuery>
{
    public GetRoleByIdQueryValidator()
    {
        RuleFor(x => x.RoleId)
            .NotEmpty()
            .WithMessage("Role ID is required.");
    }
}
