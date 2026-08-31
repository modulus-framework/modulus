using System.Data.Common;
using Dapper;
using TradeFlow.Modules.Identity.Application.Permissions.Dtos;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Shared.Application.Data;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Permissions.Queries;

internal sealed class GetPermissionByCodeQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetPermissionByCodeQuery, Result<PermissionResponse>>
{
    public async Task<Result<PermissionResponse>> HandleAsync(
        GetPermissionByCodeQuery request,
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
            WHERE code = @Code";

        PermissionResponse? permission = await connection.QueryFirstOrDefaultAsync<PermissionResponse>(
            sql,
            new { Code = request.Code });

        if (permission is null)
        {
            return Result.Failure<PermissionResponse>(IdentityErrors.Permission.NotFound);
        }

        return Result.Success(permission);
    }
}
