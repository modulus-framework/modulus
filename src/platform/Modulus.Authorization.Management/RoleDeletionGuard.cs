namespace Modulus.Authorization.Management;

using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Grants;

/// <summary>Refuses to delete a role that still holds an allow or deny grant (the clearance check of premodule 1, D-P1).</summary>
internal sealed class RoleDeletionGuard(EfPermissionGrantStore store) : IRoleDeletionGuard
{
    public async Task<string?> BlockingReasonAsync(string role, CancellationToken cancellationToken)
    {
        var count = (await store.GetGrantsForHolderAsync(GrantHolderType.Role, role, cancellationToken)).Count;
        return count == 0
            ? null
            : $"The role still holds {count} permission grant(s). Clear them first with PUT /api/identity/roles/{role}/permissions and an empty list.";
    }
}
