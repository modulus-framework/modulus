using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Shared.Application.Abstractions.Oidc;

/// <summary>
/// Interface for token blacklisting services
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>
    /// Blacklists a token until its natural expiration
    /// </summary>
    /// <param name="tokenId">The token ID to blacklist</param>
    /// <param name="expiration">The token expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    Task<Result> BlacklistTokenAsync(string tokenId, DateTime expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a token is blacklisted
    /// </summary>
    /// <param name="tokenId">The token ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the token is blacklisted</returns>
    Task<bool> IsTokenBlacklistedAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if all tokens for a user are blacklisted
    /// </summary>
    /// <param name="userId">The user ID whose tokens should be checked</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if all user tokens are blacklisted</returns>
    Task<bool> AreAllUserTokensBlacklistedAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired tokens from the blacklist
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    Task<Result> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Blacklists all tokens for a specific user
    /// </summary>
    /// <param name="userId">The user ID whose tokens should be blacklisted</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    Task<Result> BlacklistAllUserTokensAsync(string userId, CancellationToken cancellationToken = default);
}
