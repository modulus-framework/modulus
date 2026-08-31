using TradeFlow.Modules.Identity.Application.Permissions.Dtos;

using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Permissions.Queries;

public sealed record GetEligibleSupervisorsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SupervisorDto>>>;
