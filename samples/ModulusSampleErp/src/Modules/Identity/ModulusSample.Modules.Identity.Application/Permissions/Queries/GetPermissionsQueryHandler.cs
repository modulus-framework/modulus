using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Permissions.Dtos;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

internal sealed class GetPermissionsQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetPermissionsQuery, Result<PermissionListResponse>>
{
    public async Task<Result<PermissionListResponse>> HandleAsync(
        GetPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql = @"
            SELECT
                code AS Code,
                name AS Name,
                description AS Description,
                category AS Category,
                created_at_utc AS CreatedAtUtc,
                is_active AS IsActive
            FROM identity.permissions
            ORDER BY category, name";

        IEnumerable<PermissionResponse> permissions = await connection.QueryAsync<PermissionResponse>(sql);

        return Result.Success(new PermissionListResponse(permissions.ToList()));
    }
}
