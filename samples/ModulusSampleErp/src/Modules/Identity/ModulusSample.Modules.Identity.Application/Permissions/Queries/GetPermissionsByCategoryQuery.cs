using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Permissions.Dtos;

using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record GetPermissionsByCategoryQuery(string Category) : Modulus.Mediator.Abstractions.IQuery<Result<PermissionCategoryResponse>>;
