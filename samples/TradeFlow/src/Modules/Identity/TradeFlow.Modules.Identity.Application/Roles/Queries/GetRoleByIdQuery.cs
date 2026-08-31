using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Modules.Identity.Application.Roles.Dtos;
using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Roles.Queries;

[RequirePermission(AppPermissions.IdentityRoleManageAll)]
public sealed record GetRoleByIdQuery(Guid RoleId) : Modulus.Mediator.Abstractions.IQuery<Result<RoleDetailResponse>>;
