using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record UpdateSettingCommand(
    Guid SettingId,
    string? Category,
    string? Description,
    bool? IsPublic) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;
