using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Permissions.Queries;

/// <summary>
/// Query to get the current user's roles and permissions
/// </summary>
public sealed record GetMyPermissionsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<MyPermissionsResponse>>;
