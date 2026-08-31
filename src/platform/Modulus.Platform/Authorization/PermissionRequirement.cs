namespace Modulus.Authorization;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Modulus.Authorization.Grants;

/// <summary>
/// The authorization requirement behind the <c>:</c>-permission policy convention
/// (<see cref="ModulusPermissionPolicyProvider"/>): the principal must hold
/// <see cref="Permission"/>.
/// </summary>
/// <remarks>
/// The decision is <b>server-resolved</b>: the principal's effective permission
/// set is computed through <see cref="IPermissionResolver"/> (grant store,
/// implication closure, deny-override, and — when enabled — delegation) from the
/// identity and role claims. A grant created or revoked at runtime, e.g. through
/// the authorization management API, therefore takes effect at the endpoint on
/// the very next request with no token re-issue. A <c>permission</c> claim
/// carried on the principal is honoured as an additional source for hosts that
/// mint fine-grained claims (the identity seeder, header-driven test auth) — but
/// an explicit store <c>Deny</c> for that permission (exact or covering wildcard)
/// always wins over the claim, so revoking a grant takes effect immediately even
/// for a principal holding a stale token (blueprint §5.1, §22).
/// </remarks>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>Creates the requirement for <paramref name="permission"/>.</summary>
    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <summary>The permission the principal must hold.</summary>
    public string Permission { get; }
}

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/> with the semantics documented on
/// the requirement. Registered as a singleton — resolver and store are
/// stateless/thread-safe, and identity is read from the evaluated principal
/// rather than the ambient <c>HttpContext</c>, so the handler also serves
/// imperative <c>IAuthorizationService.AuthorizeAsync(user, …)</c> checks.
/// </summary>
internal sealed class PermissionRequirementHandler(
    IPermissionResolver resolver, IPermissionGrantStore grantStore)
    : AuthorizationHandler<PermissionRequirement>
{
    private const string WildcardSuffix = ":*";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var query = BuildQuery(principal);

        // ONE store read: raw grants feed both the deny check below and the
        // resolver (which must not re-read the store for them).
        var grants = grantStore.GetGrants(query);

        // A store-level Deny for this permission always wins, even over a
        // token-embedded permission claim — otherwise a revoked grant would
        // stay effective until the caller's token expires. Checked against the
        // raw grants (not the resolved set) so a covering wildcard deny also
        // blocks a matching claim.
        if (IsExplicitlyDenied(grants, requirement.Permission))
            return Task.CompletedTask;

        if (principal.HasClaim("permission", requirement.Permission)
            || resolver.Resolve(query, grants).Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private bool IsExplicitlyDenied(IReadOnlyCollection<PermissionGrant> grants, string permission)
    {
        foreach (var grant in grants)
        {
            if (grant.Type != PermissionGrantType.Deny)
                continue;

            if (string.Equals(grant.Permission, permission, StringComparison.OrdinalIgnoreCase))
                return true;

            if (grant.Permission.EndsWith(WildcardSuffix, StringComparison.Ordinal)
                && permission.StartsWith(
                    grant.Permission[..^1], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PrincipalGrantQuery BuildQuery(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value;
        Guid? userId = Guid.TryParse(sub, out var id) ? id : null;

        // Accept BOTH the long and short role claim types: principals built
        // from tokens validated with MapInboundClaims=false (OpenIddict, most
        // OIDC library defaults) keep the literal "role" name, while
        // ASP.NET-normalised identities carry ClaimTypes.Role.
        var roles = principal.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .Distinct()
            .ToArray();

        return new PrincipalGrantQuery(userId, roles);
    }
}
