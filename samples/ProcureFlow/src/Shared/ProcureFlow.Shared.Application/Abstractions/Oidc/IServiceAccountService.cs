using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Shared.Application.Abstractions.Oidc;

/// <summary>
/// Interface for service account token management
/// </summary>
public interface IServiceAccountService
{
    /// <summary>
    /// Gets a new service account token using client credentials flow
    /// </summary>
    /// <param name="clientId">The service account client ID</param>
    /// <param name="clientSecret">The service account client secret</param>
    /// <param name="scopes">The scopes to request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the token response</returns>
    Task<Result<OidcTokenResponse>> GetServiceAccountTokenAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached service account token if available and valid
    /// </summary>
    /// <param name="clientId">The service account client ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cached token response if available and valid, null otherwise</returns>
    Task<OidcTokenResponse?> GetCachedServiceAccountTokenAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches a service account token for future use
    /// </summary>
    /// <param name="clientId">The service account client ID</param>
    /// <param name="tokenResponse">The token response to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    Task<Result> CacheServiceAccountTokenAsync(
        string clientId,
        OidcTokenResponse tokenResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a cached service account token
    /// </summary>
    /// <param name="clientId">The service account client ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    Task<Result> InvalidateServiceAccountTokenAsync(
        string clientId,
        CancellationToken cancellationToken = default);
}
