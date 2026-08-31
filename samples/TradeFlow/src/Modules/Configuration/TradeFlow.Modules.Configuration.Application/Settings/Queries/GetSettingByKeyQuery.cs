using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Queries;

public sealed record GetSettingByKeyQuery(string Key) : Modulus.Mediator.Abstractions.IQuery<Result<SettingResponse>>;
