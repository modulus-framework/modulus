using TradeFlow.Modules.Identity.Application.Abstractions.Identity;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Default implementation of rate limiting service using Redis cache.
/// </summary>
internal sealed class RateLimitingService(ICacheService cacheService) : IRateLimitingService
{
    public async Task<Result<bool>> IsAllowedAsync(Guid userId, string actionType, TimeSpan duration, CancellationToken ct = default)
    {
        string cacheKey = GetCacheKey(userId, actionType);
        object? cachedValue = await cacheService.GetAsync<object>(cacheKey, ct);

        if (cachedValue is not null)
        {
            return Result.Failure<bool>(
                Error.Validation("RateLimit.Exceeded", "Rate limit exceeded for this action."));
        }

        return Result.Success(true);
    }

    public async Task RecordActionAsync(Guid userId, string actionType, TimeSpan duration, CancellationToken ct = default)
    {
        string cacheKey = GetCacheKey(userId, actionType);
        await cacheService.SetAsync(cacheKey, new object(), duration, ct);
    }

    public async Task<TimeSpan?> GetRemainingTimeAsync(Guid userId, string actionType, CancellationToken ct = default)
    {
        // ICacheService doesn't expose TTL, so we return null
        // The handler should use a generic message for retry time
        await Task.CompletedTask;
        return null;
    }

    private static string GetCacheKey(Guid userId, string actionType) =>
        $"rate_limit:{actionType}:{userId}";
}
