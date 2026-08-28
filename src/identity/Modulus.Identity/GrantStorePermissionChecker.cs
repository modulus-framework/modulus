using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore.Http;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;

/// <summary>
/// Bridges <see cref="ICurrentUser.HasPermission"/> to the server-side grant store:
/// resolves the current principal's effective permission set from its <em>identity</em>
/// (user id + role claims) rather than trusting fine-grained "permission" claims on
/// the token (blueprint §22). Registered as scoped so the resolved set is computed at
/// most once per request. Fail-closed: an unauthenticated principal resolves to no
/// permissions.
/// </summary>
internal sealed class GrantStorePermissionChecker(
    IHttpContextAccessor httpContextAccessor,
    IPermissionResolver resolver) : IPermissionChecker
{
    private IReadOnlySet<string>? _effective;

    public bool HasPermission(string permission)
        => (_effective ??= Resolve()).Contains(permission);

    public IReadOnlyCollection<string> GetEffectivePermissions()
        => _effective ??= Resolve();

    private IReadOnlySet<string> Resolve()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return EmptySet;

        return resolver.Resolve(new PrincipalGrantQuery(ReadUserId(principal), ReadRoles(principal)));
    }

    private static Guid? ReadUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // Accept BOTH the long and short role claim types: principals built from
    // tokens validated with MapInboundClaims=false (OpenIddict, most OIDC
    // library defaults) keep the literal "role" name, while ASP.NET-normalised
    // identities carry ClaimTypes.Role.
    private static IReadOnlyCollection<string> ReadRoles(ClaimsPrincipal principal)
        => principal.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .Distinct()
            .ToArray();

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
