using FluentValidation;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed class DeleteSettingCommandValidator : AbstractValidator<DeleteSettingCommand>
{
    public DeleteSettingCommandValidator()
    {
        RuleFor(x => x.SettingId)
            .NotEmpty().WithMessage("SettingId is required");
    }
}
