namespace Modulus.Authorization.Grants;

using Modulus.Core.Abstractions;

/// <summary>
/// Default <see cref="IPermissionResolver"/>. Resolves against an
/// <see cref="IPermissionGrantStore"/> and the frozen <see cref="IPermissionRegistry"/>.
/// Stateless and thread-safe — registered as <b>scoped</b>: the grant store seam
/// carries a request-scoped caching registration, so the resolver must be built
/// inside the consuming scope to observe it (a singleton would pin a root-scope
/// cache that serves stale grants until restart).
/// </summary>
public sealed class PermissionResolver(
    IPermissionGrantStore grantStore,
    IPermissionRegistry registry,
    TimeProvider? clock = null) : IPermissionResolver
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private const string WildcardSuffix = ":*";

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Snapshot of permission → prerequisites. Built once, lazily: by the time any
    // permission is resolved (request time) the registry is frozen, so caching the
    // Requires graph avoids re-scanning GetAll() on every implication step.
    private readonly Lazy<IReadOnlyDictionary<string, string[]>> _requiresIndex =
        new(() =>
        {
            var index = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in registry.GetAll())
                index[definition.Permission] = definition.Requires;
            return index;
        });

    public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Resolve(principal, grantStore.GetGrants(principal));
    }

    /// <inheritdoc />
    public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal, IReadOnlyCollection<PermissionGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(grants);

        if (grants.Count == 0)
            return EmptySet;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = _clock.GetUtcNow();

        foreach (var grant in grants)
        {
            // Expiry is decided here, per decision, so a lapsed grant stops applying with no administrator action (BR-005).
            // A restriction narrows a permission's scope; it never makes a permission effective nor removes it.
            if (!grant.IsValidAt(now) || grant.Type is PermissionGrantType.Restrict)
                continue;

            var target = grant.Type is PermissionGrantType.Allow ? allowed : denied;
            if (IsWildcard(grant.Permission))
                ExpandWildcard(grant.Permission, target, includeCritical: grant.Type is PermissionGrantType.Deny);
            else
                target.Add(grant.Permission);
        }

        // Implication closure: an allowed permission confers everything it requires.
        ExpandImplications(allowed);

        // Deny-override: explicit denials win, applied after the closure.
        if (denied.Count > 0)
            allowed.ExceptWith(denied);

        return allowed;
    }

    /// <summary>
    /// The permissions explicitly denied to <paramref name="principal"/> (wildcard denies expanded). Authority that
    /// reaches the principal by another route (a delegation) is filtered against this set, so a deny wins over every source.
    /// </summary>
    public IReadOnlySet<string> ResolveDenied(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return ResolveDenied(grantStore.GetGrants(principal));
    }

    /// <inheritdoc cref="ResolveDenied(PrincipalGrantQuery)"/>
    public IReadOnlySet<string> ResolveDenied(IReadOnlyCollection<PermissionGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = _clock.GetUtcNow();
        foreach (var grant in grants)
        {
            if (grant.Type is not PermissionGrantType.Deny || !grant.IsValidAt(now))
                continue;

            if (IsWildcard(grant.Permission))
                ExpandWildcard(grant.Permission, denied, includeCritical: true);
            else
                denied.Add(grant.Permission);
        }

        return denied;
    }

    private static bool IsWildcard(string permission)
        => permission.EndsWith(WildcardSuffix, StringComparison.Ordinal);

    private void ExpandWildcard(string wildcard, HashSet<string> into, bool includeCritical)
    {
        // "module:group:*" → prefix "module:group:"; matches registered permissions
        // that start with the prefix. Fail-closed: unknown prefixes add nothing.
        var prefix = wildcard[..^1]; // drop the trailing '*', keep the ':'
        foreach (var definition in registry.GetAll())
        {
            // A wildcard never confers a Critical permission; a wildcard deny still removes it.
            if (!includeCritical && definition.Sensitivity is PermissionSensitivity.Critical)
                continue;

            if (definition.Permission.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                into.Add(definition.Permission);
        }
    }

    private void ExpandImplications(HashSet<string> allowed)
    {
        // Walk the Requires graph from every granted permission, adding each
        // prerequisite. The result set itself guards against cycles.
        var index = _requiresIndex.Value;
        var pending = new Stack<string>(allowed);
        while (pending.Count > 0)
        {
            var permission = pending.Pop();
            if (!index.TryGetValue(permission, out var requires))
                continue;

            foreach (var required in requires)
            {
                if (allowed.Add(required))
                    pending.Push(required);
            }
        }
    }
}
