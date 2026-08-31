using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Dtos;

public sealed record PublicSettingResponse(
    string Key,
    string Value,
    string Category);
