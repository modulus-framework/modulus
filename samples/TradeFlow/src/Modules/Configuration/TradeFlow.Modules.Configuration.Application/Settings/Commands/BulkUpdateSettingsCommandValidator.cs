using FluentValidation;
using TradeFlow.Modules.Configuration.Application.Settings.Commands;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed class BulkUpdateSettingsCommandValidator : AbstractValidator<BulkUpdateSettingsCommand>
{
    public BulkUpdateSettingsCommandValidator()
    {
        RuleFor(x => x.SettingUpdates)
            .NotEmpty().WithMessage("SettingUpdates is required")
            .Must(x => x.Count <= 100).WithMessage("Cannot update more than 100 settings at once");
    }
}
