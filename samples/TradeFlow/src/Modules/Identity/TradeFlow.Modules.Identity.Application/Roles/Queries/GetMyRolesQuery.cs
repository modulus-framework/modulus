using TradeFlow.Modules.Identity.Application.Roles.Dtos;

using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Roles.Queries;

/// <summary>
/// Query to get the current user's roles with full details
/// </summary>
public sealed record GetMyRolesQuery() : Modulus.Mediator.Abstractions.IQuery<Result<MyRolesResponse>>;
