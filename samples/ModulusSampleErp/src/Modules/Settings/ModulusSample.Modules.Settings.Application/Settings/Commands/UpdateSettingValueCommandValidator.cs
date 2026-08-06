using FluentValidation;
using ModulusSample.Modules.Settings.Application.Settings.Commands;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

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