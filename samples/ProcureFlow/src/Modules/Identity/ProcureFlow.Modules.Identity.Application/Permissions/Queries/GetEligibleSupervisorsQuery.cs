using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Permissions.Queries;

public sealed record GetEligibleSupervisorsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SupervisorDto>>>;
