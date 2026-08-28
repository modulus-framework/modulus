using System.Data.Common;
using Dapper;
using ProcureFlow.Modules.Identity.Application.Abstractions.Authentication;
using ProcureFlow.Modules.Identity.Application.Roles.Dtos;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Application.Data;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Application.Roles.Queries;

internal sealed class GetMyRolesQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IUserContext userContext)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetMyRolesQuery, Result<MyRolesResponse>>
{
    public async Task<Result<MyRolesResponse>> HandleAsync(
        GetMyRolesQuery request,
        CancellationToken cancellationToken)
    {
        UserId userId = userContext.UserId;

        if (userId == UserId.Empty)
        {
            return Result.Failure<MyRolesResponse>(IdentityErrors.User.NotFound);
        }

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql = @"
            SELECT
                r.id AS RoleId,
                r.name AS Name,
                r.description AS Description,
                r.is_system AS IsSystem
            FROM identity.roles r
            INNER JOIN identity.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @UserId";

        IEnumerable<RoleRow> roleRows = await connection.QueryAsync<RoleRow>(sql, new { UserId = userId.Value });

        var roleList = roleRows.ToList();

        var roleDetails = new List<RoleDetailInfo>();

        foreach (RoleRow role in roleList)
        {
            const string permissionsSql = @"
                SELECT p.code
                FROM identity.permissions p
                INNER JOIN identity.role_permissions rp ON rp.permission_id = p.id
                WHERE rp.role_id = @RoleId AND rp.is_active = true";

            IEnumerable<string> perms = await connection.QueryAsync<string>(permissionsSql, new { RoleId = role.RoleId });

            roleDetails.Add(new RoleDetailInfo(
                role.RoleId,
                role.Name,
                role.Description ?? string.Empty,
                role.IsSystem,
                perms));
        }

        return Result.Success(new MyRolesResponse(userId.Value, roleDetails));
    }

    private sealed record RoleRow(Guid RoleId, string Name, string? Description, bool IsSystem);
}
