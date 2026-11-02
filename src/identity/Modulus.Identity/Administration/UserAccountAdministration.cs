using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Extensions;

namespace Modulus.Identity.Administration;

/// <summary>What an account change did.</summary>
public enum AccountChangeOutcome
{
    /// <summary>The change was made and audited.</summary>
    Changed,

    /// <summary>No account has that id.</summary>
    NotFound,

    /// <summary>The caller's <c>If-Match</c> names an older state than the account is in now.</summary>
    Stale,

    /// <summary>Only a Tenant Owner grants or revokes the Tenant Owner role.</summary>
    OwnerRequired,

    /// <summary>The last active Tenant Owner cannot be demoted; another owner must be named first.</summary>
    LastOwnerDemoted,

    /// <summary>The last active Tenant Owner cannot be locked; another owner must be named first.</summary>
    LastOwnerLocked,

    /// <summary>The store refused the change. <see cref="AccountChangeResult{TUser}.Errors"/> says why, by field.</summary>
    Rejected,
}

/// <summary>The result of an account change: the outcome, the account when it exists, and the field errors when it was refused.</summary>
public sealed record AccountChangeResult<TUser>(AccountChangeOutcome Outcome, TUser? User, IReadOnlyDictionary<string, string[]>? Errors = null)
    where TUser : ModulusUser;

/// <summary>
/// The rules for changing an account's roles, lock and unlock. These are the rules the admin endpoints apply, in one place, so the endpoints and
/// any other caller (for example an application's command handlers) get the same answers: only a Tenant Owner grants or revokes the owner role;
/// the last active owner is never demoted or locked; a change must match the account's current state when the caller sends <c>If-Match</c>; and
/// every change is audited with the acting person.
/// </summary>
public sealed class UserAccountAdministration<TUser>(UserManager<TUser> users, ISecurityAuditLog? audit = null)
    where TUser : ModulusUser
{
    /// <summary>Replaces the account's roles with <paramref name="roles"/>. Owner rules apply to the Tenant Owner role.</summary>
    public async Task<AccountChangeResult<TUser>> SetRolesAsync(Guid userId, IEnumerable<string> roles, Guid actor, IReadOnlyList<string> ifMatch, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return new(AccountChangeOutcome.NotFound, null);
        if (IsStale(ifMatch, user))
            return new(AccountChangeOutcome.Stale, user);

        var current = await users.GetRolesAsync(user);
        var desired = roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();
        var grantsOwner = toAdd.Contains(AccountLifecycleExtensions.TenantOwnerRole, StringComparer.OrdinalIgnoreCase);
        var revokesOwner = toRemove.Contains(AccountLifecycleExtensions.TenantOwnerRole, StringComparer.OrdinalIgnoreCase);

        // Ownership is not inherited from administration: only an owner grants or revokes it.
        if ((grantsOwner || revokesOwner) && await users.FindByIdAsync(actor.ToString()) is { } actingUser
            && !await users.IsInRoleAsync(actingUser, AccountLifecycleExtensions.TenantOwnerRole))
        {
            return new(AccountChangeOutcome.OwnerRequired, user);
        }

        // The last active owner is kept: demoting it needs another owner first (an approved ownership transfer).
        if (revokesOwner && await AccountLifecycleExtensions.IsLastActiveTenantOwnerAsync(users, user))
            return new(AccountChangeOutcome.LastOwnerDemoted, user);

        if (toAdd.Count > 0)
        {
            var added = await users.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded)
                return Rejected(user, "roles", added.Errors);
        }
        if (toRemove.Count > 0)
        {
            var removed = await users.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
                return Rejected(user, "roles", removed.Errors);
        }

        // Role changes do not move the concurrency stamp by themselves; a plain update does, so the next If-Match sees the change.
        var stamped = await users.UpdateAsync(user);
        if (!stamped.Succeeded)
            return Rejected(user, "roles", stamped.Errors);

        Audit(actor, "identity.user.roles.changed", user.Id, $"added={string.Join(',', toAdd)};removed={string.Join(',', toRemove)}");
        if (grantsOwner)
            Audit(actor, "identity.tenant-owner.granted", user.Id, null);
        if (revokesOwner)
            Audit(actor, "identity.tenant-owner.revoked", user.Id, null);
        return new(AccountChangeOutcome.Changed, user);
    }

    /// <summary>Locks the account until <paramref name="until"/> (indefinitely when null). The last active owner cannot be locked.</summary>
    public async Task<AccountChangeResult<TUser>> LockAsync(Guid userId, DateTimeOffset? until, string reason, Guid actor, IReadOnlyList<string> ifMatch, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return new(AccountChangeOutcome.NotFound, null);
        if (IsStale(ifMatch, user))
            return new(AccountChangeOutcome.Stale, user);

        // Locking the last active owner would leave the company without an administrator of its own permissions.
        if (await AccountLifecycleExtensions.IsLastActiveTenantOwnerAsync(users, user))
            return new(AccountChangeOutcome.LastOwnerLocked, user);

        await users.SetLockoutEnabledAsync(user, true);
        var set = await users.SetLockoutEndDateAsync(user, until ?? DateTimeOffset.MaxValue);
        if (!set.Succeeded)
            return Rejected(user, "lock", set.Errors);

        Audit(actor, "identity.user.locked", user.Id, $"until={(until?.ToString("O") ?? "indefinite")};reason={reason.Trim()}");
        return new(AccountChangeOutcome.Changed, user);
    }

    /// <summary>Clears the account's lock.</summary>
    public async Task<AccountChangeResult<TUser>> UnlockAsync(Guid userId, Guid actor, IReadOnlyList<string> ifMatch, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return new(AccountChangeOutcome.NotFound, null);
        if (IsStale(ifMatch, user))
            return new(AccountChangeOutcome.Stale, user);

        var cleared = await users.SetLockoutEndDateAsync(user, null);
        if (!cleared.Succeeded)
            return Rejected(user, "lock", cleared.Errors);

        Audit(actor, "identity.user.unlocked", user.Id, null);
        return new(AccountChangeOutcome.Changed, user);
    }

    /// <summary>The account's current entity tag, as the admin API sends it in <c>ETag</c>.</summary>
    public static string ETagOf(TUser user) => $"\"{user.ConcurrencyStamp}\"";

    // True when the caller's If-Match names none of the account's current states (and is not "*").
    private static bool IsStale(IReadOnlyList<string> ifMatch, TUser user) =>
        ifMatch.Count > 0 && !ifMatch.Contains("*") && !ifMatch.Contains(ETagOf(user));

    private static AccountChangeResult<TUser> Rejected(TUser user, string field, IEnumerable<IdentityError> errors) =>
        new(AccountChangeOutcome.Rejected, user, new Dictionary<string, string[]> { [field] = errors.Select(e => e.Description).ToArray() });

    private void Audit(Guid actor, string action, Guid target, string? details)
    {
        var values = new Dictionary<string, string?>();
        if (details is not null)
            values["detail"] = details;
        audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Actor = actor.ToString("D"),
            Target = $"user:{target:D}",
            Details = values,
        });
    }
}
