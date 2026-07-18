namespace Modulus.Authorization.Grants;

using Modulus.Core.Abstractions;

/// <summary>
/// Default <see cref="IPermissionResolver"/>. Resolves against an
/// <see cref="IPermissionGrantStore"/> and the frozen <see cref="IPermissionRegistry"/>.
/// Stateless and thread-safe — registered as a singleton.
/// </summary>
public sealed class PermissionResolver(
    IPermissionGrantStore grantStore,
    IPermissionRegistry registry) : IPermissionResolver
{
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

        var grants = grantStore.GetGrants(principal);
        if (grants.Count == 0)
            return EmptySet;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in grants)
        {
            var target = grant.Type is PermissionGrantType.Allow ? allowed : denied;
            if (IsWildcard(grant.Permission))
                ExpandWildcard(grant.Permission, target);
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

    private static bool IsWildcard(string permission)
        => permission.EndsWith(WildcardSuffix, StringComparison.Ordinal);

    private void ExpandWildcard(string wildcard, HashSet<string> into)
    {
        // "module:group:*" → prefix "module:group:"; matches registered permissions
        // that start with the prefix. Fail-closed: unknown prefixes add nothing.
        var prefix = wildcard[..^1]; // drop the trailing '*', keep the ':'
        foreach (var definition in registry.GetAll())
        {
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
