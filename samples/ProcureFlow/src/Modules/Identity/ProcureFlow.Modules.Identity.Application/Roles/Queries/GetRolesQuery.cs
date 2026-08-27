using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Modules.Identity.Application.Roles.Dtos;
using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Roles.Queries;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record GetRolesQuery : Modulus.Mediator.Abstractions.IQuery<Result<List<RoleResponse>>>;
