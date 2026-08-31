using FluentValidation;
using TradeFlow.Modules.Configuration.Application.Settings.Commands;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed class DeleteSettingCommandValidator : AbstractValidator<DeleteSettingCommand>
{
    public DeleteSettingCommandValidator()
    {
        RuleFor(x => x.SettingId)
            .NotEmpty().WithMessage("SettingId is required");
    }
}
