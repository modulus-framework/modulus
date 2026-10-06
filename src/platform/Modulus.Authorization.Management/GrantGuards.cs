using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Management;

/// <summary>The rules that stop the administration API from becoming a privilege-escalation path.</summary>
internal static class GrantGuards
{
    private const string WildcardSuffix = ":*";

    /// <summary>The caller's user id (<c>NameIdentifier</c> or <c>sub</c>), or null.</summary>
    public static Guid? UserIdOf(ClaimsPrincipal caller)
        => Guid.TryParse(caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value, out var id) ? id : null;

    /// <summary>
    /// Expands <paramref name="requested"/> against the permission catalog: wildcards become every registered permission under
    /// their prefix. Names that match nothing are returned in <paramref name="unknown"/>; they are refused, never stored,
    /// because a typo would otherwise sit in the grant table waiting for a future permission to collide with it.
    /// </summary>
    public static List<string> Expand(IPermissionRegistry registry, IEnumerable<string?> requested, out List<string> unknown)
    {
        var all = registry.GetAll();
        var expanded = new List<string>();
        unknown = [];
        foreach (var raw in requested)
        {
            var permission = raw?.Trim();
            if (string.IsNullOrEmpty(permission))
            {
                unknown.Add(raw ?? string.Empty);
                continue;
            }

            if (permission.EndsWith(WildcardSuffix, StringComparison.Ordinal))
            {
                var prefix = permission[..^1];
                var matches = all.Where(d => d.Sensitivity is not PermissionSensitivity.Critical
                                             && d.Permission.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Permission).ToList();
                if (matches.Count == 0)
                    unknown.Add(permission);
                else
                    expanded.AddRange(matches);
            }
            else if (registry.Exists(permission))
            {
                expanded.Add(permission);
            }
            else
            {
                unknown.Add(permission);
            }
        }

        return expanded;
    }

    /// <summary>The permissions in <paramref name="permissions"/> the caller does not hold (BR-012: you can grant only what you have).</summary>
    public static async Task<List<string>> NotHeldAsync(
        ClaimsPrincipal caller, IAuthorizationService authorization, IEnumerable<string> permissions)
    {
        if ((await authorization.AuthorizeAsync(caller, AuthorizationManagementExtensions.GrantAnyPermission)).Succeeded)
            return [];

        var missing = new List<string>();
        foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!(await authorization.AuthorizeAsync(caller, permission)).Succeeded)
                missing.Add(permission);
        }

        return missing;
    }

    /// <summary>Whether the grant targets the caller (their own user id, or a role they hold): BR-011, nobody widens their own access.</summary>
    public static bool TargetsCaller(ClaimsPrincipal caller, GrantHolderType holderType, string holder)
        => holderType is GrantHolderType.User
            ? UserIdOf(caller) is { } me && Guid.TryParse(holder, out var target) && me == target
            : caller.IsInRole(holder) || caller.HasClaim(c => c.Type is "role" or ClaimTypes.Role && string.Equals(c.Value, holder, StringComparison.OrdinalIgnoreCase));

    public static IResult Unknown(IEnumerable<string> names)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["permissions"] = [$"Not a registered permission: {string.Join(", ", names)}."],
        });

    public static IResult Refused(string detail)
        => Results.Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden);
}
