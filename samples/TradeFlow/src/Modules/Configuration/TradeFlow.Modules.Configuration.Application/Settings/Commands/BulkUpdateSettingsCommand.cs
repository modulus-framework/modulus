using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record BulkUpdateSettingsCommand(
    Dictionary<Guid, string> SettingUpdates) : Modulus.Mediator.Abstractions.ICommand<Result<int>>;
