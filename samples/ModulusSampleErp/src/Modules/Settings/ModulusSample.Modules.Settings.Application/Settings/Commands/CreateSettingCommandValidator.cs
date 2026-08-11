using FluentValidation;
using ModulusSample.Modules.Settings.Application.Settings.Commands;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed class CreateSettingCommandValidator : AbstractValidator<CreateSettingCommand>
{
    public CreateSettingCommandValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required")
            .MaximumLength(256).WithMessage("Key cannot exceed 256 characters")
            .Matches("^[^\\s]+$").WithMessage("Key cannot contain whitespace characters");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Value is required");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required")
            .MaximumLength(100).WithMessage("Category cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required");
    }
}
