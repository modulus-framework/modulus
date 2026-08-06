using ModulusSample.Modules.Identity.Application.Permissions.Dtos;

using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

public sealed record GetEligibleSupervisorsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SupervisorDto>>>;
