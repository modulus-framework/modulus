using System.Data.Common;
using Dapper;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Modules.Identity.Domain.Errors;
using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Shared.Application.Data;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityUserViewAll)]
public sealed record GetAdminUserByIdQuery(Guid UserId) : Modulus.Mediator.Abstractions.IQuery<Result<AdminUserDetailResponse>>;

internal sealed class GetAdminUserByIdQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetAdminUserByIdQuery, Result<AdminUserDetailResponse>>
{
    public async Task<Result<AdminUserDetailResponse>> HandleAsync(
        GetAdminUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string userSql = @"
            SELECT
                id AS UserId,
                email AS Email,
                user_name AS UserName,
                first_name AS FirstName,
                last_name AS LastName,
                phone_number AS PhoneNumber,
                profile_image_url AS ProfileImageUrl,
                user_type AS UserType,
                status AS Status,
                email_confirmed AS EmailConfirmed,
                phone_number_confirmed AS PhoneNumberConfirmed,
                created_at_utc AS CreatedAtUtc,
                last_login_at_utc AS LastLoginAtUtc
            FROM identity.users
            WHERE id = @UserId AND is_deleted = false";

        UserProfileResponse? user = await connection.QueryFirstOrDefaultAsync<UserProfileResponse>(
            userSql,
            new { request.UserId });

        if (user is null)
        {
            return Result.Failure<AdminUserDetailResponse>(IdentityErrors.User.NotFound);
        }

        const string rolesSql = @"
            SELECT
                r.id AS RoleId,
                r.name AS Name,
                r.description AS Description,
                r.is_system AS IsSystem,
                ur.assigned_at_utc AS AssignedAtUtc
            FROM identity.user_roles ur
            INNER JOIN identity.roles r ON r.id = ur.role_id
            WHERE ur.user_id = @UserId
            ORDER BY r.name";

        List<AdminUserRoleDto> roles = (await connection.QueryAsync<AdminUserRoleDto>(
            rolesSql,
            new { request.UserId })).AsList();

        return Result.Success(new AdminUserDetailResponse(
            user.UserId,
            user.Email,
            user.UserName,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.ProfileImageUrl,
            user.UserType,
            user.Status,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            roles));
    }
}
