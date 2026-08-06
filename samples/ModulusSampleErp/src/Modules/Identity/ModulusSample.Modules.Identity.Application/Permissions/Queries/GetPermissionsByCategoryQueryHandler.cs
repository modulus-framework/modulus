using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Permissions.Dtos;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Permissions.Queries;

internal sealed class GetPermissionsByCategoryQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetPermissionsByCategoryQuery, Result<PermissionCategoryResponse>>
{
    public async Task<Result<PermissionCategoryResponse>> HandleAsync(
        GetPermissionsByCategoryQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Category))
        {
            return Result.Failure<PermissionCategoryResponse>(IdentityErrors.Permission.InvalidCode);
        }

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
            WHERE category = @Category
            ORDER BY name";

        IEnumerable<PermissionResponse> permissions = await connection.QueryAsync<PermissionResponse>(
            sql,
            new { Category = request.Category });

        var permissionList = permissions.ToList();

        if (permissionList.Count == 0)
        {
            return Result.Failure<PermissionCategoryResponse>(IdentityErrors.Permission.NotFound);
        }

        return Result.Success(new PermissionCategoryResponse(
            request.Category,
            permissionList.Count,
            permissionList));
    }
}
