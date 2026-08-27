using System.Data.Common;
using Dapper;
using ProcureFlow.Modules.Identity.Application.Roles.Dtos;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Application.Data;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Roles.Queries;

internal sealed class GetRoleByIdQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetRoleByIdQuery, Result<RoleDetailResponse>>
{
    public async Task<Result<RoleDetailResponse>> HandleAsync(
        GetRoleByIdQuery request,
        CancellationToken cancellationToken)
    {
        var roleId = RoleId.Create(request.RoleId);

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string roleSql = @"
            SELECT
                id AS RoleId,
                name AS Name,
                description AS Description,
                is_system AS IsSystem,
                created_at_utc AS CreatedAtUtc
            FROM identity.roles
            WHERE id = @RoleId";

        RoleRow? roleRow = await connection.QueryFirstOrDefaultAsync<RoleRow>(roleSql, new { RoleId = roleId.Value });

        if (roleRow is null)
        {
            return Result.Failure<RoleDetailResponse>(IdentityErrors.Role.NotFound);
        }

        const string permissionsSql = @"
            SELECT p.code
            FROM identity.permissions p
            INNER JOIN identity.role_permissions rp ON rp.permission_id = p.id
            WHERE rp.role_id = @RoleId AND rp.is_active = true";

        IEnumerable<string> permissionCodes = await connection.QueryAsync<string>(
            permissionsSql,
            new { RoleId = roleId.Value });

        var response = new RoleDetailResponse(
            roleRow.RoleId,
            roleRow.Name,
            roleRow.Description ?? string.Empty,
            roleRow.IsSystem,
            permissionCodes.ToList(),
            roleRow.CreatedAtUtc);

        return Result.Success(response);
    }

    private sealed record RoleRow(Guid RoleId, string Name, string? Description, bool IsSystem, DateTime CreatedAtUtc);
}
