using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record UpdateSettingValueCommand(
    Guid SettingId,
    string NewValue) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;
