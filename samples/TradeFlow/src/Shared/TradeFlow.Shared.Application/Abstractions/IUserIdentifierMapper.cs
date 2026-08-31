using TradeFlow.Shared.Domain;

namespace TradeFlow.Shared.Application.Abstractions;

/// <summary>
/// Interface for mapping external user identifiers to application user IDs
/// Used by claims transformations to resolve external provider IDs to internal user IDs
/// </summary>
public interface IUserIdentifierMapper
{
    /// <summary>
    /// Maps an external user ID to an application user ID
    /// </summary>
    /// <param name="externalId">The external user ID (e.g., Keycloak sub claim)</param>
    /// <param name="provider">The external provider name (e.g., "Keycloak", "Google")</param>
    /// <param="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the application user ID if successful</returns>
    Task<Result<Guid>> GetApplicationUserIdFromExternalIdAsync(
        string externalId,
        string provider,
        CancellationToken cancellationToken = default);
}
