using FluentValidation;
using ModulusSample.Modules.Features.Application.Features.Commands;

namespace ModulusSample.Modules.Features.Application.Features.Commands;

public sealed class CreateFeatureFlagValidator : AbstractValidator<CreateFeatureFlagCommand>
{
    public CreateFeatureFlagValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MaximumLength(256).WithMessage("Key cannot exceed 256 characters");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");
    }
}

public sealed class UpdateFeatureFlagValidator : AbstractValidator<UpdateFeatureFlagCommand>
{
    public UpdateFeatureFlagValidator()
    {
        RuleFor(x => x.FeatureFlagId)
            .NotEmpty().WithMessage("Feature flag ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");
    }
}

public sealed class ToggleFeatureFlagValidator : AbstractValidator<ToggleFeatureFlagCommand>
{
    public ToggleFeatureFlagValidator()
    {
        RuleFor(x => x.FeatureFlagId)
            .NotEmpty().WithMessage("Feature flag ID is required");
    }
}
