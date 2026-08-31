using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record DeleteSettingCommand(Guid SettingId) : Modulus.Mediator.Abstractions.ICommand<Result>;
