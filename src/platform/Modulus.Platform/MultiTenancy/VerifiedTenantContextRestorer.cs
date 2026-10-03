using System.Collections.Concurrent;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy;

/// <summary>
/// <see cref="ITenantContextRestorer"/> registered by <c>AddMultiTenancy</c>: resolves the id through
/// <see cref="ITenantStore"/> (active tenants only) and returns the full <see cref="TenantInfo"/>
/// (slug, group, connection string), or throws <see cref="TenantContextRejectedException"/>.
/// Lookups are cached for <see cref="CacheDuration"/>, so deactivating a tenant stops its queued
/// messages and jobs within that window.
/// </summary>
public sealed class VerifiedTenantContextRestorer(
    ITenantStore store,
    TimeProvider? time = null) : ITenantContextRestorer
{
    /// <summary>How long a successful lookup is reused.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, (TenantInfo Tenant, DateTimeOffset Expires)> _cache = new();

    public async ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        if (!_cache.TryGetValue(tenantId, out var hit) || hit.Expires <= now)
        {
            var tenant = await store.FindByIdAsync(tenantId, ct).ConfigureAwait(false)
                ?? throw new TenantContextRejectedException(tenantId);
            hit = (tenant, now + CacheDuration);
            _cache[tenantId] = hit;
        }

        return hit.Tenant;
    }
}
