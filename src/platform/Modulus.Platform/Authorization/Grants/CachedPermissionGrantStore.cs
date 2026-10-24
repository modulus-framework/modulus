using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Grants;

/// <summary>
/// Request-scoped wrapper around a singleton <see cref="IPermissionGrantStore"/> that
/// memoizes <see cref="GetGrants"/> per principal, eliminating redundant store lookups
/// within a single request. Multiple authorization checks against the same principal
/// reuse the cached result instead of re-querying.
///
/// Registered as scoped, this wraps a singleton store and caches its results for the
/// request lifetime. The underlying store is injected by concrete type to avoid
/// circular dependency with the interface registration.
/// </summary>
/// <remarks>
/// The memo key includes the ambient company and its lineage (<see cref="ICurrentTenant"/>). Grants differ per company, so a
/// request that changes company (or enters the host context) must not reuse the grants read for the earlier company.
/// </remarks>
public sealed class CachedPermissionGrantStore : IPermissionGrantStore
{
    private readonly IPermissionGrantStore _inner;
    private readonly ICurrentTenant? _tenant;
    private readonly Dictionary<string, IReadOnlyCollection<PermissionGrant>> _cache = [];

    /// <summary>Constructor for InMemory store wrapping.</summary>
    public CachedPermissionGrantStore(InMemoryPermissionGrantStore inner, ICurrentTenant? tenant = null)
    {
        _inner = inner;
        _tenant = tenant;
    }

    /// <summary>Generic constructor for any IPermissionGrantStore implementation.</summary>
    public CachedPermissionGrantStore(IPermissionGrantStore inner, ICurrentTenant? tenant = null)
    {
        _inner = inner;
        _tenant = tenant;
    }

    public async Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(PrincipalGrantQuery principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var key = CacheKey(principal);
        if (_cache.TryGetValue(key, out var cached))
            return cached;
        var grants = await _inner.GetGrantsAsync(principal, ct).ConfigureAwait(false);
        _cache[key] = grants;
        return grants;
    }

    public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Create a cache key from the principal's user id and roles
        var key = CacheKey(principal);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var grants = _inner.GetGrants(principal);
        _cache[key] = grants;
        return grants;
    }

    /// <summary>
    /// Creates a deterministic cache key from the ambient company lineage, then the principal's identity
    /// (user id + sorted roles). Same principal in the same company always produces the same key.
    /// </summary>
    private string CacheKey(PrincipalGrantQuery principal)
    {
        // User ID is the primary key; roles are secondary and order-independent
        var userId = principal.UserId?.ToString() ?? "";
        var rolesPart = string.Join("|", principal.Roles.OrderBy(r => r));
        return $"{CompanyKey()}#{userId}:{rolesPart}";
    }

    private string CompanyKey()
    {
        if (_tenant is null)
            return "";
        if (_tenant.IsHost)
            return "host";
        return string.Join(",", _tenant.Lineage);
    }
}
