using FluentValidation;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed class UpdateSettingCommandValidator : AbstractValidator<UpdateSettingCommand>
{
    public UpdateSettingCommandValidator()
    {
        RuleFor(x => x.SettingId)
            .NotEmpty().WithMessage("SettingId is required");

        RuleFor(x => x.Category)
            .NotEmpty().When(x => x.Category != null)
            .MaximumLength(100).When(x => x.Category != null)
            .WithMessage("Category cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => x.Description != null)
            .WithMessage("Description cannot exceed 500 characters");
    }
}
