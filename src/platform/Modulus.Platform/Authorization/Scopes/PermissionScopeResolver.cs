namespace Modulus.Authorization.Scopes;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;

/// <summary>Who the grants of the current request are looked up for (user id + role names from the verified identity).</summary>
public interface IPrincipalGrantQuerySource
{
    /// <summary>The current principal's grant query; <see cref="PrincipalGrantQuery.Anonymous"/> when nobody is signed in.</summary>
    PrincipalGrantQuery Current { get; }
}

/// <summary>Reads the principal of the current HTTP request (user id, roles); anonymous outside a request.</summary>
internal sealed class HttpPrincipalGrantQuerySource(IHttpContextAccessor accessor) : IPrincipalGrantQuerySource
{
    public PrincipalGrantQuery Current
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return PrincipalGrantQuery.Anonymous;

            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
            var roles = principal.Claims
                .Where(c => c.Type is ClaimTypes.Role or "role")
                .Select(c => c.Value)
                .Distinct()
                .ToArray();
            return new PrincipalGrantQuery(Guid.TryParse(sub, out var id) ? id : null, roles);
        }
    }
}

/// <summary>Works out which records a permission covers for the current principal: the "same permission, different data" half of authorization.</summary>
public interface IPermissionScopeResolver
{
    /// <summary>
    /// What <paramref name="permission"/> covers for the current principal, after validity windows, explicit denies, restrictions
    /// and delegations. Fail-closed: not granted, denied, expired or unknown all give <see cref="ResolvedScope.None"/>.
    /// </summary>
    ResolvedScope Resolve(string permission);

    /// <summary>The same for an arbitrary principal (diagnostics, what-if, reports).</summary>
    ResolvedScope Resolve(PrincipalGrantQuery principal, string permission);
}

/// <summary>
/// Default <see cref="IPermissionScopeResolver"/>. Rules: grants union (Own ∪ Assigned); a restriction adds a constraint to every
/// alternative; an unscoped deny wins over everything including delegations; a delegation lends the delegator's own scope, so the
/// delegate's reach is capped by what the delegator holds; an allow covers the permissions it implies (<c>Requires</c>) at the same scope.
/// </summary>
public sealed class PermissionScopeResolver(
    IPrincipalGrantQuerySource principal,
    IPermissionGrantStore grants,
    IPermissionRegistry registry,
    IServiceProvider services,
    TimeProvider? clock = null) : IPermissionScopeResolver
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, ResolvedScope> _memo = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<IReadOnlyDictionary<string, string[]>> _requires = new(() =>
        registry.GetAll().ToDictionary(d => d.Permission, d => d.Requires, StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, string[]>);

    /// <inheritdoc />
    public ResolvedScope Resolve(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (_memo.TryGetValue(permission, out var known))
            return known;

        return _memo[permission] = Resolve(principal.Current, permission);
    }

    /// <inheritdoc />
    public ResolvedScope Resolve(PrincipalGrantQuery query, string permission)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (!registry.Exists(permission))
            return ResolvedScope.None;

        return ResolveCore(query, permission, _clock.GetUtcNow(), withDelegations: true);
    }

    private ResolvedScope ResolveCore(PrincipalGrantQuery query, string permission, DateTimeOffset now, bool withDelegations)
    {
        var valid = grants.GetGrants(query).Where(g => g.IsValidAt(now)).ToList();

        // Deny wins over every source, delegations included (BR-004), and ignores scope: it removes the permission.
        if (valid.Any(g => g.Type is PermissionGrantType.Deny && Covers(g.Permission, permission)))
            return ResolvedScope.None;

        var scope = ResolvedScope.Of(valid
            .Where(g => g.Type is PermissionGrantType.Allow && Covers(g.Permission, permission, includeCritical: false))
            .Select(g => g.EffectiveScope.Kind is ScopeKind.Tenant ? ScopeClause.Everything : new ScopeClause([g.EffectiveScope])));

        if (withDelegations && query.UserId is { } userId && services.GetService<IDelegationStore>() is { } delegations)
        {
            foreach (var delegation in delegations.ActiveFor(userId, now))
            {
                if (!delegation.Permissions.Contains(permission))
                    continue;

                // The delegate gets the delegator's scope for it, never more (FR-GRT-005); never re-delegated onward.
                scope = scope.Union(ResolveCore(
                    new PrincipalGrantQuery(delegation.FromUserId, delegation.FromRoles), permission, now, withDelegations: false));
            }
        }

        if (scope.IsNone)
            return scope;

        foreach (var restriction in valid.Where(g => g.Type is PermissionGrantType.Restrict && Covers(g.Permission, permission)))
            scope = scope.Restrict(restriction.EffectiveScope);

        return scope;
    }

    // A grant covers the permission it names, every permission under a wildcard prefix, and whatever those imply.
    private bool Covers(string granted, string permission, bool includeCritical = true)
    {
        if (granted.EndsWith(":*", StringComparison.Ordinal))
        {
            var prefix = granted[..^1];
            return registry.GetAll().Any(d => (includeCritical || d.Sensitivity is not PermissionSensitivity.Critical)
                                              && d.Permission.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                              && Implies(d.Permission, permission));
        }

        return Implies(granted, permission);
    }

    private bool Implies(string held, string permission)
    {
        if (string.Equals(held, permission, StringComparison.OrdinalIgnoreCase))
            return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { held };
        var pending = new Stack<string>([held]);
        while (pending.Count > 0)
        {
            if (!_requires.Value.TryGetValue(pending.Pop(), out var required))
                continue;

            foreach (var next in required)
            {
                if (string.Equals(next, permission, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (seen.Add(next))
                    pending.Push(next);
            }
        }

        return false;
    }
}
