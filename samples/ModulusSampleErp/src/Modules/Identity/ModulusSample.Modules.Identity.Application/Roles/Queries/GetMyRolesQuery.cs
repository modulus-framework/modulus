using ModulusSample.Modules.Identity.Application.Roles.Dtos;

using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Roles.Queries;

/// <summary>
/// Query to get the current user's roles with full details
/// </summary>
public sealed record GetMyRolesQuery() : Modulus.Mediator.Abstractions.IQuery<Result<MyRolesResponse>>;
