using FluentValidation;
using ModulusSample.Modules.Tenants.Application.Tenants.Commands;

namespace ModulusSample.Modules.Tenants.Application.Tenants.Validators;

public sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.Subdomain)
            .NotEmpty().WithMessage("Subdomain is required")
            .Matches(@"^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$").WithMessage("Subdomain must be alphanumeric with hyphens only, 3-63 characters, starting and ending with alphanumeric");

        RuleFor(x => x.DatabaseConnectionString)
            .NotEmpty().WithMessage("Database connection string is required")
            .MaximumLength(2000).WithMessage("Connection string cannot exceed 2000 characters");
    }
}

public sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.DatabaseConnectionString)
            .NotEmpty().WithMessage("Database connection string is required")
            .MaximumLength(2000).WithMessage("Connection string cannot exceed 2000 characters");
    }
}

public sealed class UpdateTenantFeaturesValidator : AbstractValidator<UpdateTenantFeaturesCommand>
{
    public UpdateTenantFeaturesValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant ID is required");
    }
}

public sealed class UpdateTenantSettingsValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant ID is required");
    }
}