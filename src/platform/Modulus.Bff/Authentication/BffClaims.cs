namespace Modulus.Bff.Authentication;

using System.Security.Claims;
using System.Text.Json;

/// <summary>
/// Reads the claims every supported auth server spells differently, so the rest of the BFF
/// (policies, rate-limit partitions, <c>/bff/user</c>) sees one shape:
/// <list type="bullet">
/// <item>client id: <c>client_id</c> (OpenIddict, Duende, Keycloak), <c>azp</c> (Entra, Keycloak, Auth0), <c>cid</c> (Okta), <c>appid</c> (Entra v1)</item>
/// <item>scopes: <c>scope</c> or <c>scp</c>, space-separated or one claim per value</item>
/// <item>roles: <c>role</c>, <c>roles</c> (Entra), <c>groups</c> (Okta, Authentik), Keycloak's <c>realm_access.roles</c> JSON, plus configured types</item>
/// </list>
/// </summary>
public static class BffClaims
{
    /// <summary>The normalized role claim type.</summary>
    public const string Role = "role";

    /// <summary>The normalized name claim type.</summary>
    public const string Name = "name";

    private static readonly string[] s_clientIdTypes = ["client_id", "azp", "cid", "appid"];
    private static readonly string[] s_scopeTypes = ["scope", "scp", "http://schemas.microsoft.com/identity/claims/scope"];

    /// <summary>The OAuth client the token was issued to, or <c>null</c>.</summary>
    public static string? GetClientId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        foreach (var type in s_clientIdTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    /// <summary>The granted scopes, whatever claim shape the server used.</summary>
    public static IReadOnlySet<string> GetScopes(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.Claims)
        {
            if (!s_scopeTypes.Contains(claim.Type, StringComparer.Ordinal))
                continue;
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                scopes.Add(scope);
        }

        return scopes;
    }

    /// <summary>The claim types read as roles for <paramref name="server"/> (before configured extras).</summary>
    public static IReadOnlyList<string> DefaultRoleClaimTypes(BffAuthServer server) => server switch
    {
        BffAuthServer.Keycloak => [Role, "roles", "realm_access"],
        BffAuthServer.Okta or BffAuthServer.Authentik => [Role, "roles", "groups"],
        BffAuthServer.AzureAd => [Role, "roles", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"],
        BffAuthServer.Generic => [Role, "roles", "groups", "realm_access"],
        _ => [Role, "roles"],
    };

    /// <summary>
    /// Returns a principal whose identity uses <c>name</c>/<c>role</c> as name and role claim
    /// types and carries one <c>role</c> claim per role found in any of <paramref name="roleClaimTypes"/>
    /// (JSON arrays and Keycloak's <c>{"roles":[...]}</c> objects are unpacked). A missing
    /// <c>name</c> falls back to <c>preferred_username</c>, then <c>email</c>.
    /// </summary>
    public static ClaimsPrincipal Normalize(ClaimsPrincipal principal, IEnumerable<string> roleClaimTypes, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(roleClaimTypes);

        var roleTypes = new HashSet<string>(roleClaimTypes, StringComparer.Ordinal);
        var claims = new List<Claim>();
        var roles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var claim in principal.Claims)
        {
            if (roleTypes.Contains(claim.Type))
            {
                foreach (var role in ReadValues(claim.Value))
                    roles.Add(role);
                if (claim.Type == Role)
                    continue;
            }

            claims.Add(new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer));
        }

        if (!claims.Exists(c => c.Type == Name))
        {
            var fallback = principal.FindFirst("preferred_username")?.Value ?? principal.FindFirst("email")?.Value;
            if (!string.IsNullOrEmpty(fallback))
                claims.Add(new Claim(Name, fallback));
        }

        claims.AddRange(roles.Select(r => new Claim(Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, Name, Role));
    }

    private static IEnumerable<string> ReadValues(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length == 0)
            return [];
        if (trimmed[0] is not ('[' or '{'))
            return [value];

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("roles", out var nested))
                root = nested;
            return root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [value];
        }
    }
}
