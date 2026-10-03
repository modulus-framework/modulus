using System.Reflection;
using Microsoft.Extensions.Caching.Hybrid;

namespace Modulus.Mediator.Behaviors;

using Modulus.Caching;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Evicts the cached query results tagged by a command's
/// <see cref="InvalidatesCacheAttribute"/> once the command has succeeded.
/// </summary>
/// <remarks>
/// Registered outside <see cref="TransactionBehavior{TRequest,TResponse}"/>, so
/// the eviction happens after the commit: evicting earlier would let a
/// concurrent query re-cache the pre-commit state. A failed command evicts
/// nothing. Tags are tenant-scoped (<see cref="CacheKeys"/>), matching
/// <see cref="CachingBehavior{TRequest,TResponse}"/>. Without a registered
/// <see cref="HybridCache"/> this is a pass-through.
/// </remarks>
public sealed class CacheInvalidationBehavior<TRequest, TResponse>(
    HybridCache? hybridCache = null,
    ICurrentTenant? currentTenant = null) : IPipelineBehavior<TRequest, TResponse>
{
    private static readonly string[] s_tags =
        typeof(TRequest).GetCustomAttribute<InvalidatesCacheAttribute>()?.Tags
            .Where(t => !string.IsNullOrEmpty(t))
            .ToArray() ?? [];

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var result = await next();
        if (s_tags.Length == 0 || hybridCache is null)
            return result;

        foreach (var tag in s_tags)
            await hybridCache.RemoveByTagAsync(CacheKeys.Tag(currentTenant, tag), ct);
        return result;
    }
}
