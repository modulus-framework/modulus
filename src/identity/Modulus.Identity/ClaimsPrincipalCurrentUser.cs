using System.Security.Claims;

namespace Modulus.Identity;

using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

/// <summary>
/// Implements ICurrentUser by reading claims from the current HttpContext principal.
/// Registered as scoped; resolves the current user per-request.
/// </summary>
internal sealed class ClaimsPrincipalCurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IPermissionChecker? permissionChecker = null)
    : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? UserName =>
        Principal?.Identity?.Name
        ?? Principal?.FindFirst("preferred_username")?.Value;

    public string? Email =>
        Principal?.FindFirst(ClaimTypes.Email)?.Value
        ?? Principal?.FindFirst("email")?.Value;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) =>
        Principal?.IsInRole(role) ?? false;

    public bool HasPermission(string permission)
    {
        if (permissionChecker is not null)
            return permissionChecker.HasPermission(permission);

        // Fallback: honour the "permission" claims on the principal directly
        // (e.g. issued by the Modulus role/permission seeder) so permissions
        // resolve even when no custom IPermissionChecker is registered.
        return Principal?.HasClaim("permission", permission) ?? false;
    }

    public IReadOnlyList<string> Permissions =>
        permissionChecker is not null
            ? permissionChecker.GetEffectivePermissions().ToList()
            // Fallback: same claim-based reasoning as HasPermission's fallback —
            // resolve from the token when no store-backed checker is registered.
            : Principal?.FindAll("permission")
                .Select(c => c.Value)
                .ToList()
            ?? (IReadOnlyList<string>)[];
}

/// <summary>
/// Optional: check permissions against a store (e.g. role-based permission table).
/// Falls back to claim-based check when not registered.
/// </summary>
public interface IPermissionChecker
{
    bool HasPermission(string permission);

    /// <summary>
    /// The principal's full effective permission set (blueprint §22) — backs
    /// <see cref="ICurrentUser.Permissions"/> so it reflects live grant-store
    /// state (implication closure, wildcards, deny-override, delegation) the
    /// same way <see cref="HasPermission"/> already does, instead of trusting
    /// whatever "permission" claims happen to be baked into the token.
    /// </summary>
    IReadOnlyCollection<string> GetEffectivePermissions();
}
