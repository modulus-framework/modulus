namespace Modulus.Authorization.Management;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Audit;
using Modulus.Authorization.Dimensions;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;


/// <summary>Body of <c>PUT {role}/permissions</c>: the complete set of permissions the role should hold (allow grants).</summary>
/// <param name="Permissions">The permissions the role should hold. Permissions it holds and this list omits are revoked.</param>
public sealed record RolePermissionsRequest(IReadOnlyList<string>? Permissions);

/// <summary>
/// Role-permission administration (premodule 1, decision D-P1). A role's allow grants are read and set here, through the same grant store
/// and the same rules as <c>POST /grants</c>: nobody grants to a role they hold, an administrator grants only what they hold unless they
/// carry <c>authorization:grant-any</c>, a Critical permission needs <c>authorization:grant-any</c> to grant or revoke, a grant that
/// completes a toxic combination is refused, and every change is audited. Deny grants are shown and left alone by this route.
/// </summary>
public static class RolePermissionEndpoints
{
    /// <summary>Maps <c>GET {prefix}/{role}/permissions</c> and <c>PUT {prefix}/{role}/permissions</c> (default <c>/api/identity/roles</c>).</summary>
    public static RouteGroupBuilder MapModulusRolePermissions(this IEndpointRouteBuilder endpoints, string prefix = "/api/identity/roles")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AuthorizationManagementExtensions.ManagePermission);

        // The company's own grants (what a PUT changes) and the grants it inherits from the companies above it (read-only).
        group.MapGet("/{role}/permissions", async (string role, EfPermissionGrantStore store, CancellationToken ct) =>
        {
            var grants = await store.GetGrantsForHolderAsync(GrantHolderType.Role, role, ct);
            var inherited = await store.GetInheritedGrantsForHolderAsync(GrantHolderType.Role, role, ct);
            return Results.Ok(new
            {
                own = grants.Select(g => new { permission = g.Permission, type = g.Type.ToString() }).OrderBy(g => g.permission, StringComparer.Ordinal),
                inherited = inherited.Select(i => new { companyId = i.CompanyId, permission = i.Grant.Permission, type = i.Grant.Type.ToString() })
                    .OrderBy(g => g.permission, StringComparer.Ordinal),
            });
        });

        group.MapPut("/{role}/permissions", async (
            string role, RolePermissionsRequest body, ClaimsPrincipal caller,
            EfPermissionGrantStore store, IPermissionRegistry registry, IAuthorizationService authorization,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, ISodPolicy sodPolicy,
            [FromServices] IUserRoleDirectory? roleDirectory, IEffectiveAccessService? effectiveAccessService, CancellationToken ct) =>
        {
            var desired = (body.Permissions ?? [])
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var current = (await store.GetGrantsForHolderAsync(GrantHolderType.Role, role, ct))
                .Where(g => g.Type is PermissionGrantType.Allow)
                .Select(g => g.Permission)
                .ToList();
            var toGrant = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
            var toRevoke = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();
            var target = $"{GrantHolderType.Role}:{role}";
            var grantAny = (await authorization.AuthorizeAsync(caller, AuthorizationManagementExtensions.GrantAnyPermission)).Succeeded;

            if (toGrant.Count > 0)
            {
                // Only permissions modules declared in code can be granted (C-2).
                var expanded = GrantGuards.Expand(registry, toGrant, out var unknown);
                if (unknown.Count > 0)
                    return GrantGuards.Unknown(unknown);

                // BR-011: nobody widens their own access, not even through a role they already hold.
                if (GrantGuards.TargetsCaller(caller, GrantHolderType.Role, role))
                    return await AuthorizationManagementExtensions.RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "self-grant", "You cannot grant permissions to a role you hold.", ct);

                // Critical permissions are granted only by holders of grant-any.
                var critical = expanded.Where(p => registry.GetAll().Any(d => d.Permission == p && d.Sensitivity == PermissionSensitivity.Critical)).ToList();
                if (critical.Count > 0 && !grantAny)
                    return await AuthorizationManagementExtensions.RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "critical", $"Only grant-any holders can grant Critical permissions: {string.Join(", ", critical)}.", ct);

                // BR-012: an administrator grants only what they hold, unless they carry grant-any.
                var notHeld = await GrantGuards.NotHeldAsync(caller, authorization, expanded);
                if (notHeld.Count > 0)
                    return await AuthorizationManagementExtensions.RefuseAsync(auditWriter, currentUser, "Grant", target,
                        "above-ceiling", $"You cannot grant permissions you do not hold: {string.Join(", ", notHeld)}.", ct);

                // A grant must not complete a toxic combination (FR-WFL-004).
                var proposed = await AuthorizationManagementExtensions.ProposedPermissionsAsync(
                    GrantHolderType.Role, role, null, expanded, store, roleDirectory, effectiveAccessService, ct);
                var violations = sodPolicy.Evaluate(proposed.Permissions);
                if (violations.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        error = "SoD violation",
                        message = "Granting these permissions would create segregation-of-duties violations.",
                        violations = violations.Select(v => new { constraint = v.Constraint.Name, held = v.HeldPermissions }).ToList(),
                    });
                }
            }

            if (toRevoke.Count > 0 && !grantAny)
            {
                var criticalRevoke = toRevoke.Where(p => registry.GetAll().Any(d => d.Permission == p && d.Sensitivity == PermissionSensitivity.Critical)).ToList();
                if (criticalRevoke.Count > 0)
                    return await AuthorizationManagementExtensions.RefuseAsync(auditWriter, currentUser, "Revoke", target,
                        "critical", $"Only grant-any holders can revoke Critical permissions: {string.Join(", ", criticalRevoke)}.", ct);
            }

            if (toGrant.Count > 0)
                await store.GrantToRoleAsync(role, toGrant, ct);
            foreach (var permission in toRevoke)
                await store.RevokeFromRoleAsync(role, permission, ct);

            if (toGrant.Count > 0)
            {
                await AuthorizationManagementExtensions.EmitAuditAsync(auditWriter, observers, currentUser, "Grant", "Granted", target,
                    new Dictionary<string, string> { ["permissions"] = string.Join(",", toGrant) }, ct);
            }
            if (toRevoke.Count > 0)
            {
                await AuthorizationManagementExtensions.EmitAuditAsync(auditWriter, observers, currentUser, "Revoke", "Revoked", target,
                    new Dictionary<string, string> { ["permissions"] = string.Join(",", toRevoke) }, ct);
            }

            return Results.Ok(new { granted = toGrant, revoked = toRevoke });
        });

        return group;
    }
}
