using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Queries;

public sealed record GetAllSettingsQuery(
    string? Category = null,
    bool? IsPublic = null,
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<SettingResponse>>>;
