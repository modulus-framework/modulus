namespace Modulus.Caching;

using Modulus.Core.Abstractions;

/// <summary>
/// The one tenant-scoped cache key scheme every Modulus cache uses
/// (<c>MemoryCacheService</c>, <c>RedisCacheService</c>, <c>FusionCacheService</c>
/// and the mediator's query cache), so an entry or tag written through one is
/// found, or invalidated, through another.
/// </summary>
/// <remarks>
/// A resolved tenant gets its own partition, so the same key or tag in two
/// tenants never collides (tenant A can neither read nor invalidate tenant B's
/// entries). The host context and an app without multi-tenancy share the flat,
/// unprefixed partition.
/// </remarks>
public static class CacheKeys
{
    /// <summary>Tenant-scoped key for a cache entry.</summary>
    public static string Entry(ICurrentTenant? tenant, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return tenant is { IsHost: false, TenantId: { } tenantId }
            ? $"modulus:entry:{tenantId:N}:{key}"
            : $"modulus:entry:{key}";
    }

    /// <summary>Tenant-scoped name for a cache tag.</summary>
    public static string Tag(ICurrentTenant? tenant, string tag)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        return tenant is { IsHost: false, TenantId: { } tenantId }
            ? $"modulus:tag:{tenantId:N}:{tag}"
            : $"modulus:tag:{tag}";
    }
}
