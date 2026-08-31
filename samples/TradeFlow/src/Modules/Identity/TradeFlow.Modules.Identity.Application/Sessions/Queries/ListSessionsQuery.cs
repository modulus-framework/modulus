using TradeFlow.Modules.Identity.Application.Sessions.Dtos;

using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Sessions.Queries;

public sealed record ListSessionsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<SessionDto>>>;
