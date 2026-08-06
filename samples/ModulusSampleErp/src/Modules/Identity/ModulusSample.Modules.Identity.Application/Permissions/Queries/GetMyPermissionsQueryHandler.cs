using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Application.Permissions.Dtos;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using AuthRoles = ModulusSample.Modules.Identity.Domain.Authorization.Roles;
using AuthRolePriority = ModulusSample.Modules.Identity.Domain.Authorization.RolePriority;

namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

internal sealed class GetMyPermissionsQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IUserContext userContext,
    ICacheService cacheService)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetMyPermissionsQuery, Result<MyPermissionsResponse>>
{
    public async Task<Result<MyPermissionsResponse>> HandleAsync(
        GetMyPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        UserId userId = userContext.UserId;

        if (userId == UserId.Empty)
        {
            return Result.Failure<MyPermissionsResponse>(IdentityErrors.User.NotFound);
        }

        MyPermissionsResponse response = await cacheService.GetOrCreateAsync(
            CacheKeys.User.MyPermissionsResponse(userId.Value),
            async () =>
            {
                await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

                const string userSql = @"
                    SELECT
                        u.user_type AS UserType,
                        u.email_confirmed AS EmailConfirmed,
                        u.phone_number_confirmed AS PhoneNumberConfirmed,
                        u.status AS Status
                    FROM identity.users u
                    WHERE u.id = @UserId AND u.is_deleted = false";

                UserInfoRow? userInfo = await connection.QueryFirstOrDefaultAsync<UserInfoRow>(userSql, new { UserId = userId.Value });

                if (userInfo is null)
                {
                    return null!;
                }

                const string permissionsSql = @"
                    SELECT DISTINCT p.code
                    FROM identity.permissions p
                    INNER JOIN identity.role_permissions rp ON rp.permission_id = p.id AND rp.is_active = true
                    INNER JOIN identity.user_roles ur ON ur.role_id = rp.role_id
                    WHERE ur.user_id = @UserId";

                IEnumerable<string> permissionCodes = await connection.QueryAsync<string>(permissionsSql, new { UserId = userId.Value });

                const string rolesSql = @"
                    SELECT r.id AS Id, r.name AS Name
                    FROM identity.roles r
                    INNER JOIN identity.user_roles ur ON ur.role_id = r.id
                    WHERE ur.user_id = @UserId";

                IEnumerable<RoleRow> roleRows = await connection.QueryAsync<RoleRow>(rolesSql, new { UserId = userId.Value });

                var roleDtos = roleRows
                    .Select(r => new RoleDto(r.Id, r.Name, GetRolePriority(r.Name)))
                    .ToList();

                RoleDto? primaryRole = roleDtos
                    .OrderByDescending(r => r.Priority)
                    .FirstOrDefault();

                Dictionary<string, object> userMetadata = new()
                {
                    ["emailVerified"] = userInfo.EmailConfirmed,
                    ["phoneVerified"] = userInfo.PhoneNumberConfirmed,
                    ["accountStatus"] = userInfo.Status
                };

                return new MyPermissionsResponse(
                    UserId: userId.Value,
                    UserType: userInfo.UserType,
                    PrimaryRole: primaryRole,
                    UserMetadata: userMetadata,
                    Roles: roleDtos,
                    Permissions: permissionCodes.OrderBy(p => p).ToList());
            },
            TimeSpan.FromMinutes(15),
            cancellationToken);

        if (response is null)
        {
            return Result.Failure<MyPermissionsResponse>(IdentityErrors.User.NotFound);
        }

        return Result.Success(response);
    }

    private static int GetRolePriority(string roleName) => roleName switch
    {
        AuthRoles.Admin => AuthRolePriority.Admin,
        AuthRoles.User => AuthRolePriority.User,
        _ => 0
    };

    private sealed record UserInfoRow(string UserType, bool EmailConfirmed, bool PhoneNumberConfirmed, string Status);

    private sealed record RoleRow(Guid Id, string Name);
}
