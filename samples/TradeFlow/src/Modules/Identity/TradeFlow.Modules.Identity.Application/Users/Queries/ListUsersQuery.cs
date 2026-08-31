using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityAdmin)]
public sealed record ListUsersQuery(
    int PageNumber,
    int PageSize,
    string? UserType,
    string? Status,
    string? SearchTerm) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<UserListItemResponse>>>;
