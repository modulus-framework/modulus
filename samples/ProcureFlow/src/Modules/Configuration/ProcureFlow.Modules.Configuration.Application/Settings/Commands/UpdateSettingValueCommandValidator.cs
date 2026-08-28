using FluentValidation;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed class UpdateSettingValueCommandValidator : AbstractValidator<UpdateSettingValueCommand>
{
    public UpdateSettingValueCommandValidator()
    {
        RuleFor(x => x.SettingId)
            .NotEmpty().WithMessage("SettingId is required");

        RuleFor(x => x.NewValue)
            .NotEmpty().WithMessage("NewValue is required");
    }
}
