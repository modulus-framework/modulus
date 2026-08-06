using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Application.Permissions.Dtos;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using AuthRoles = ModulusSample.Modules.Identity.Domain.Authorization.Roles;
using AuthRolePriority = ModulusSample.Modules.Identity.Domain.Authorization.RolePriority;

namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

internal sealed class GetPrimaryRoleQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IUserContext userContext)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetPrimaryRoleQuery, Result<PrimaryRoleResponse>>
{
    public async Task<Result<PrimaryRoleResponse>> HandleAsync(
        GetPrimaryRoleQuery request,
        CancellationToken cancellationToken)
    {
        UserId userId = userContext.UserId;

        if (userId == UserId.Empty)
        {
            return Result.Failure<PrimaryRoleResponse>(IdentityErrors.User.NotFound);
        }

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql = @"
            SELECT r.name AS Name
            FROM identity.roles r
            INNER JOIN identity.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @UserId";

        IEnumerable<string> roleNames = await connection.QueryAsync<string>(sql, new { UserId = userId.Value });

        var roleList = roleNames.ToList();

        if (roleList.Count == 0)
        {
            return Result.Failure<PrimaryRoleResponse>(IdentityErrors.User.NoRolesAssigned);
        }

        var primaryRole = roleList
            .Select(name => new { Name = name, Priority = GetRolePriority(name) })
            .OrderByDescending(r => r.Priority)
            .First();

        string redirectUrl = GetDefaultRedirectUrl(primaryRole.Name);
        string reason = GetDefaultReason(primaryRole.Name);

        return Result.Success(new PrimaryRoleResponse(
            PrimaryRoleName: primaryRole.Name,
            Priority: primaryRole.Priority,
            RedirectUrl: redirectUrl,
            Reason: reason));
    }

    private static string GetDefaultRedirectUrl(string roleName) => roleName switch
    {
        AuthRoles.Admin => "/admin/dashboard",
        AuthRoles.User => "/",
        _ => "/"
    };

    private static string GetDefaultReason(string roleName) => roleName switch
    {
        AuthRoles.Admin => "Admin dashboard",
        AuthRoles.User => "User home",
        _ => "Home"
    };

    private static int GetRolePriority(string roleName) => roleName switch
    {
        AuthRoles.Admin => AuthRolePriority.Admin,
        AuthRoles.User => AuthRolePriority.User,
        _ => 0
    };
}
