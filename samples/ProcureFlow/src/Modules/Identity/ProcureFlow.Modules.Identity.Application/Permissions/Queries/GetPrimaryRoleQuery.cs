using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Permissions.Queries;

/// <summary>
/// Returns the caller's primary (highest-priority) role and the computed frontend redirect URL.
/// Use this endpoint when you only need routing information and don't need the full permission set.
/// </summary>
public sealed record GetPrimaryRoleQuery : Modulus.Mediator.Abstractions.IQuery<Result<PrimaryRoleResponse>>;
