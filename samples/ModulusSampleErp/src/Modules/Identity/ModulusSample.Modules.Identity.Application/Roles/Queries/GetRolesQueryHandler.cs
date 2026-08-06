using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Roles.Queries;

internal sealed class GetRolesQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    ICacheService cacheService)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetRolesQuery, Result<List<RoleResponse>>>
{
    public async Task<Result<List<RoleResponse>>> HandleAsync(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        List<RoleResponse> response = await cacheService.GetOrCreateAsync(
            CacheKeys.User.AllRoles(),
            async () =>
            {
                await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

                const string sql = @"
                    SELECT
                        r.id AS RoleId,
                        r.name AS Name,
                        r.description AS Description,
                        r.is_system AS IsSystem,
                        (SELECT CAST(COUNT(*) AS int) FROM identity.role_permissions rp
                         WHERE rp.role_id = r.id AND rp.is_active = true) AS PermissionsCount
                    FROM identity.roles r
                    ORDER BY r.name";

                IEnumerable<RoleResponse> roles = await connection.QueryAsync<RoleResponse>(sql);

                return roles.ToList();
            },
            TimeSpan.FromDays(1),
            cancellationToken);

        return Result.Success(response);
    }
}
