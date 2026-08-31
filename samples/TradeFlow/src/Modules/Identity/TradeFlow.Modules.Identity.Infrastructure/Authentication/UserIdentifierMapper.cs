using System.Data.Common;
using Dapper;
using TradeFlow.Shared.Application.Abstractions;
using TradeFlow.Shared.Application.Data;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Maps external identity-provider user IDs to application user IDs by querying the
/// <c>identity.external_logins</c> table (via the message-agnostic
/// <see cref="PostgresDbConnectionFactory"/>). Returns a not-found result when no
/// provider link exists.
/// </summary>
public sealed class UserIdentifierMapper(IDbConnectionFactory dbConnectionFactory) : IUserIdentifierMapper
{
    public async Task<Result<Guid>> GetApplicationUserIdFromExternalIdAsync(
        string externalId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(provider))
        {
            return Result.Failure<Guid>(
                Error.Validation("UserIdentifier.InvalidInput", "External ID and provider must be provided."));
        }

        await using DbConnection connection = await dbConnectionFactory.OpenReadOnlyConnectionAsync();

        Guid? userId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            """
            SELECT user_id
            FROM identity.external_logins
            WHERE provider = @Provider AND provider_user_id = @ExternalId
            LIMIT 1
            """,
            new { Provider = provider, ExternalId = externalId });

        if (userId is null || userId == Guid.Empty)
        {
            return Result.Failure<Guid>(
                Error.NotFound("User.ExternalNotFound", "No user is linked to this external identity."));
        }

        return Result.Success(userId.Value);
    }
}
