using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Modulus.Mediator.Behaviors;

using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Pipeline behavior that caches query results when the request class is
/// decorated with <see cref="CacheForAttribute"/>.
/// </summary>
/// <remarks>
/// Cache keys are scoped by the ambient tenant (via
/// <see cref="ICurrentTenant"/>) — without that, a query executed for tenant A
/// would be served verbatim to tenant B whenever the serialised request
/// parameters matched. Host-scope queries share a single "host" partition.
/// An <b>unresolved</b> tenant (multi-tenancy is on but no tenant resolved —
/// a missing header, a misconfigured resolver) gets its own "unresolved"
/// partition, distinct from "host": conflating the two would let an
/// unresolved caller be served the host's "sees every tenant" cached result,
/// the same fail-closed distinction <see cref="ICurrentTenant.IsHost"/>
/// itself draws.
/// Keys are additionally scoped by the calling user (via
/// <see cref="ICurrentUser"/>): cached results frequently embed per-user
/// authorization filtering (visibility, redaction), so serving one user's
/// cached page to another user in the same tenant would leak data across
/// users. Anonymous callers share an "anon" partition.
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(
    IMemoryCache cache,
    ICurrentTenant? currentTenant = null,
    ICurrentUser? currentUser = null) : IPipelineBehavior<TRequest, TResponse>
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

    private string BuildCacheKey(TRequest request)
    {
        var type = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        // Use JSON to serialise the request — ensures different parameter
        // values produce different keys.
        var payload = JsonSerializer.Serialize(request);

        var tenantPart = currentTenant switch
        {
            null => "host",
            { IsHost: true } => "host",
            { TenantId: { } tenantId } => tenantId.ToString(),
            _ => "unresolved",
        };
        var userPart = currentUser?.UserId?.ToString() ?? "anon";
        return $"modulus:cache:t:{tenantPart}:u:{userPart}:{type}:{payload}";
    }
}
