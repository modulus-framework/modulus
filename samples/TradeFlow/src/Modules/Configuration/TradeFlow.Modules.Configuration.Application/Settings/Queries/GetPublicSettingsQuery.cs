using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Queries;

public sealed record GetPublicSettingsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<PublicSettingResponse>>>;
