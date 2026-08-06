using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Roles.Queries;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record GetRoleByIdQuery(Guid RoleId) : Modulus.Mediator.Abstractions.IQuery<Result<RoleDetailResponse>>;
