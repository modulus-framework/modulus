using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record ListUsersQuery(
    int PageNumber,
    int PageSize,
    string? UserType,
    string? Status,
    string? SearchTerm) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<UserListItemResponse>>>;
