using Modulus.Core.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// Who may hand out roles through invitations and imports. Handing out a role hands out its permissions, so the caller
/// needs <c>authorization:grant-any</c>, the same rule the grant API applies to a role inclusion.
/// </summary>
internal static class RoleAssignmentRules
{
    // The permission name is fixed by Modulus.Authorization.Management; Identity does not reference that package.
    public const string GrantAnyPermission = "authorization:grant-any";

    /// <summary>
    /// True when the caller may assign <paramref name="roles"/>. With no roles there is nothing to grant. With no
    /// <see cref="ICurrentUser"/> registered the caller is a trusted background process, as for the field checks.
    /// </summary>
    public static bool MayAssign(ICurrentUser? currentUser, IEnumerable<string>? roles)
    {
        if (roles is null || !roles.Any())
            return true;

        return currentUser is null || currentUser.HasPermission(GrantAnyPermission);
    }
}
