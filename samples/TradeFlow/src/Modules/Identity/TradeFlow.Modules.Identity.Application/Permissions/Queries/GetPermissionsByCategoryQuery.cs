using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Modules.Identity.Application.Permissions.Dtos;

using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Permissions.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record GetPermissionsByCategoryQuery(string Category) : Modulus.Mediator.Abstractions.IQuery<Result<PermissionCategoryResponse>>;
