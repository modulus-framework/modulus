using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Modulus.Mediator.Behaviors;

using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Pipeline behavior that caches query results when the request class is
/// decorated with <see cref="CacheForAttribute"/>.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    IMemoryCache cache) : IPipelineBehavior<TRequest, TResponse>
{
    // The attribute is fixed per request type; read it once per closed generic.
    private static readonly CacheForAttribute? s_attr =
        typeof(TRequest).GetCustomAttribute<CacheForAttribute>();

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (s_attr is null)
            return await next();

        var attr = s_attr;

        var key = BuildCacheKey(request);

        if (cache.TryGetValue(key, out TResponse? cached) && cached is not null)
            return cached;

        var result = await next();
        cache.Set(key, result, TimeSpan.FromSeconds(attr.Seconds));
        return result;
    }

    private static string BuildCacheKey(TRequest request)
    {
        var type = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        // Use JSON to serialise the request — ensures different parameter
        // values produce different keys.
        var payload = JsonSerializer.Serialize(request);
        return $"modulus:cache:{type}:{payload}";
    }
}
