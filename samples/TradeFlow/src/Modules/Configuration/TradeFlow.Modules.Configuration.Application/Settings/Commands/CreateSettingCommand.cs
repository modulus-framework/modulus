using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record CreateSettingCommand(
    string Key,
    string Value,
    string Category,
    string Description,
    bool IsPublic,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<CreateSettingResponse>>;
