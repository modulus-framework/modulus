using System.Data.Common;
using Dapper;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Modules.Identity.Domain.Enums;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Shared.Application.Data;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Queries;

internal sealed class ListUsersQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<ListUsersQuery, Result<PagedResult<UserListItemResponse>>>
{
    public async Task<Result<PagedResult<UserListItemResponse>>> HandleAsync(
        ListUsersQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        var filters = new List<string> { "u.is_deleted = false" };
        var parameters = new DynamicParameters();

        Error? userTypeError = ApplyUserTypeFilter(request, filters, parameters);
        if (userTypeError is not null)
        {
            return Result<PagedResult<UserListItemResponse>>.ValidationFailure(userTypeError);
        }

        ApplySearchTermFilter(request, filters, parameters);
        ApplyStatusFilter(request, filters, parameters);

        string whereClause = string.Join(" AND ", filters);
        parameters.Add("PageSize", request.PageSize);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);

        string sql =
            $"""
             SELECT
                 u.id               AS UserId,
                 u.email             AS Email,
                 u.user_name         AS UserName,
                 CONCAT(u.first_name, ' ', u.last_name) AS FullName,
                 u.user_type         AS UserType,
                 u.status            AS Status,
                 u.email_confirmed   AS EmailConfirmed,
                 u.profile_image_url AS ProfileImageUrl,
                 u.created_at_utc    AS CreatedAtUtc,
                 COALESCE(u.last_login_at_utc, '0001-01-01'::timestamp) AS LastLoginAtUtc,
                 COALESCE(array_to_string(r.role_names, ','), '') AS RoleNames,
                 COUNT(*) OVER()     AS TotalCount
             FROM identity.users u
             LEFT JOIN LATERAL (
                 SELECT ARRAY_AGG(r.name ORDER BY r.name) AS role_names
                 FROM identity.user_roles ur
                 INNER JOIN identity.roles r ON r.id = ur.role_id
                 WHERE ur.user_id = u.id
             ) r ON true
             WHERE {whereClause}
             ORDER BY u.created_at_utc DESC
             LIMIT @PageSize OFFSET @Offset
             """;

        List<UserQueryResult> results = (await connection.QueryAsync<UserQueryResult>(sql, parameters)).AsList();

        int totalCount = results.Count > 0 ? (int)results[0].TotalCount : 0;

        var items = results.Select(r =>
        {
            string[] rolesArray = r.RoleNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
            List<string>? roles = rolesArray.Length > 0 ? [.. rolesArray] : null;
            DateTime? lastLogin = r.LastLoginAtUtc == DateTime.MinValue ? null : r.LastLoginAtUtc;
            return new UserListItemResponse(
                r.UserId, r.Email, r.UserName, r.FullName, r.UserType,
                r.Status, r.EmailConfirmed, r.CreatedAtUtc, r.ProfileImageUrl,
                lastLogin, roles);
        }).ToList();

        return Result.Success(new PagedResult<UserListItemResponse>(
            items, totalCount, request.PageNumber, request.PageSize));
    }

    private static Error? ApplyUserTypeFilter(
        ListUsersQuery request, List<string> filters, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(request.UserType) ||
            request.UserType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Enum.TryParse(request.UserType, out UserType userType))
        {
            return IdentityErrors.User.UserTypeNotValid;
        }

        filters.Add("u.user_type = @UserType");
        parameters.Add("UserType", userType.ToString());
        return null;
    }

    private static void ApplySearchTermFilter(
        ListUsersQuery request, List<string> filters, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            return;
        }

        filters.Add(
            "(u.email ILIKE @SearchTerm OR u.user_name ILIKE @SearchTerm " +
            "OR u.first_name ILIKE @SearchTerm OR u.last_name ILIKE @SearchTerm)");
        parameters.Add("SearchTerm", $"%{request.SearchTerm}%");
    }

    private static void ApplyStatusFilter(
        ListUsersQuery request, List<string> filters, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(request.Status) ||
            !Enum.TryParse<UserStatus>(request.Status, out UserStatus status))
        {
            return;
        }

        filters.Add("u.status = @Status");
        parameters.Add("Status", status.ToString());
    }

    private sealed record UserQueryResult(
        Guid UserId,
        string Email,
        string UserName,
        string FullName,
        string UserType,
        string Status,
        bool EmailConfirmed,
        string? ProfileImageUrl,
        DateTime CreatedAtUtc,
        DateTime LastLoginAtUtc,
        string RoleNames,
        long TotalCount);
}
