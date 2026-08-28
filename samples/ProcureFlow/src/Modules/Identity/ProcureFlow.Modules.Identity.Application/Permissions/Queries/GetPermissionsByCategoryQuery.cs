using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Permissions.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record GetPermissionsByCategoryQuery(string Category) : Modulus.Mediator.Abstractions.IQuery<Result<PermissionCategoryResponse>>;
