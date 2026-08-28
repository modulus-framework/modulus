using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record ListUsersQuery(
    int PageNumber,
    int PageSize,
    string? UserType,
    string? Status,
    string? SearchTerm) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<UserListItemResponse>>>;
