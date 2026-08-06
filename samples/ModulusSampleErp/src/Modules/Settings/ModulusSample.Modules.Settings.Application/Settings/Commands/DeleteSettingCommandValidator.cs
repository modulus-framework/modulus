using FluentValidation;
using ModulusSample.Modules.Settings.Application.Settings.Commands;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed class DeleteSettingCommandValidator : AbstractValidator<DeleteSettingCommand>
{
    public DeleteSettingCommandValidator()
    {
        RuleFor(x => x.SettingId)
            .NotEmpty().WithMessage("SettingId is required");
    }
}