using System.Security.Cryptography;
using System.Text;
using ProcureFlow.Shared.Application.Abstractions.Oidc;
using ProcureFlow.Shared.Application.Caching;
using ProcureFlow.Shared.Domain;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Shared.Infrastructure.Authentication;

/// <summary>
/// Redis-based implementation of token blacklisting service
/// </summary>
public sealed class TokenBlacklistService(
    ICacheService cache,
    ILogger<TokenBlacklistService> logger)
    : ITokenBlacklistService
{
    /// <inheritdoc />
    public async Task<Result> BlacklistTokenAsync(string tokenId, DateTime expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return Result.Failure(Error.Validation("TokenBlacklist.InvalidTokenId", "Token ID cannot be null or empty"));
            }

            // Use token's natural expiration as cache expiration
            string cacheKey = GetTokenCacheKey(tokenId);
            await cache.SetAsync(
                cacheKey,
                "blacklisted",
                expiration.Subtract(DateTime.UtcNow),
                cancellationToken);

            logger.LogInformation("Token {TokenId} has been blacklisted until {Expiration}", tokenId, expiration);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error blacklisting token {TokenId}", tokenId);
            return Result.Failure(Error.Failure("TokenBlacklist.Error", $"Failed to blacklist token: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsTokenBlacklistedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return false;
            }

            string cacheKey = GetTokenCacheKey(tokenId);
            string? result = await cache.GetAsync<string>(cacheKey, cancellationToken);

            bool isBlacklisted = !string.IsNullOrEmpty(result);

            if (isBlacklisted)
            {
                logger.LogDebug("Token {TokenId} is blacklisted", tokenId);
            }

            return isBlacklisted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if token {TokenId} is blacklisted", tokenId);
            // In case of error, deny access for security
            return true;
        }
    }

    /// <inheritdoc />
    public Task<Result> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting cleanup of expired blacklisted tokens");

            // With Redis, expired tokens are automatically removed by TTL mechanism
            // This method is primarily for logging and monitoring purposes

            logger.LogInformation("Expired token cleanup completed (handled by Redis TTL)");
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during cleanup of expired tokens");
            return Task.FromResult(Result.Failure(Error.Failure("TokenBlacklist.CleanupError", $"Failed to cleanup expired tokens: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public async Task<Result> BlacklistAllUserTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result.Failure(Error.Validation("TokenBlacklist.InvalidUserId", "User ID cannot be null or empty"));
            }

            // Store a user-level blacklist flag
            string cacheKey = GetUserCacheKey(userId);
            // Keep user blacklist for 24 hours
            await cache.SetAsync(
                cacheKey,
                "all-tokens-blacklisted",
                TimeSpan.FromHours(24),
                cancellationToken);

            logger.LogInformation("All tokens for user {UserId} have been blacklisted", userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error blacklisting all tokens for user {UserId}", userId);
            return Result.Failure(Error.Failure("TokenBlacklist.Error", $"Failed to blacklist user tokens: {ex.Message}"));
        }
    }

    /// <summary>
    /// Checks if all tokens for a user are blacklisted
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if all user tokens are blacklisted</returns>
    public async Task<bool> AreAllUserTokensBlacklistedAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            string cacheKey = GetUserCacheKey(userId);
            string? result = await cache.GetAsync<string>(cacheKey, cancellationToken);

            bool isBlacklisted = !string.IsNullOrEmpty(result);

            if (isBlacklisted)
            {
                logger.LogDebug("All tokens for user {UserId} are blacklisted", userId);
            }

            return isBlacklisted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if user {UserId} tokens are blacklisted", userId);
            // In case of error, deny access for security
            return true;
        }
    }

    /// <summary>
    /// Gets cache key for a specific token
    /// </summary>
    /// <param name="tokenId">The token ID</param>
    /// <returns>Cache key for token</returns>
    private static string GetTokenCacheKey(string tokenId)
    {
        // Hash token ID for security and to avoid issues with special characters
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(tokenId));
        string hash = Convert.ToBase64String(hashBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");

        return $"blacklist:token:{hash}";
    }

    /// <summary>
    /// Gets cache key for user-level blacklist
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>Cache key for user</returns>
    private static string GetUserCacheKey(string userId)
    {
        return $"blacklist:user:{userId}";
    }
}
