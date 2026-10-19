using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// The user manager <c>AddModulusIdentity</c> registers. It tells every <see cref="IAccessChangeObserver"/> when an
/// account change alters what the user may do: roles added or removed, the account disabled or deleted, or a lock-out.
/// Systems that cache a user's access outside the process (the AI connector's platform scopes) therefore drop it at once
/// instead of at expiry.
/// </summary>
/// <typeparam name="TUser">The user type.</typeparam>
internal sealed class ModulusUserManager<TUser> : UserManager<TUser>
    where TUser : ModulusUser
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;

    public ModulusUserManager(
        IUserStore<TUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<TUser> passwordHasher,
        IEnumerable<IUserValidator<TUser>> userValidators,
        IEnumerable<IPasswordValidator<TUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<TUser>> logger)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
        _services = services;
        _logger = logger;
    }

    public override async Task<IdentityResult> AddToRoleAsync(TUser user, string role)
    {
        if (IsReserved(role))
            return ReservedRole(role);
        return await NotifyOnSuccessAsync(await base.AddToRoleAsync(user, role).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.added")
            .ConfigureAwait(false);
    }

    public override async Task<IdentityResult> AddToRolesAsync(TUser user, IEnumerable<string> roles)
    {
        var list = roles.ToList();
        if (list.FirstOrDefault(IsReserved) is { } reserved)
            return ReservedRole(reserved);
        return await NotifyOnSuccessAsync(await base.AddToRolesAsync(user, list).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.added")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The <c>integration:</c> roles belong to machine clients (<see cref="IntegrationClients"/>). Refusing them here covers the admin
    /// API, SCIM and the directory group mapping, which all add roles through this manager.
    /// </summary>
    private static bool IsReserved(string role)
        => role.StartsWith(IntegrationClients.RolePrefix, StringComparison.OrdinalIgnoreCase);

    private static IdentityResult ReservedRole(string role) => IdentityResult.Failed(new IdentityError
    {
        Code = "ReservedRole",
        Description = $"The role '{role}' is reserved for integration clients and cannot be assigned to a user.",
    });

    public override async Task<IdentityResult> RemoveFromRoleAsync(TUser user, string role)
        => await NotifyOnSuccessAsync(await base.RemoveFromRoleAsync(user, role).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.removed")
            .ConfigureAwait(false);

    public override async Task<IdentityResult> RemoveFromRolesAsync(TUser user, IEnumerable<string> roles)
        => await NotifyOnSuccessAsync(await base.RemoveFromRolesAsync(user, roles).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.removed")
            .ConfigureAwait(false);

    public override async Task<IdentityResult> DeleteAsync(TUser user)
        => await NotifyOnSuccessAsync(await base.DeleteAsync(user).ConfigureAwait(false), user, AccessChangeKinds.Account, "account.deleted")
            .ConfigureAwait(false);

    public override async Task<IdentityResult> SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd)
    {
        var result = await base.SetLockoutEndDateAsync(user, lockoutEnd).ConfigureAwait(false);
        return lockoutEnd is { } end && end > DateTimeOffset.UtcNow
            ? await NotifyOnSuccessAsync(result, user, AccessChangeKinds.Account, "account.locked").ConfigureAwait(false)
            : result;
    }

    public override async Task<IdentityResult> AccessFailedAsync(TUser user)
    {
        var result = await base.AccessFailedAsync(user).ConfigureAwait(false);
        return await IsLockedOutAsync(user).ConfigureAwait(false)
            ? await NotifyOnSuccessAsync(result, user, AccessChangeKinds.Account, "account.locked").ConfigureAwait(false)
            : result;
    }

    public override async Task<IdentityResult> CreateAsync(TUser user)
    {
        // A new account has no stored state yet: its lifecycle and flag must agree before it is first saved.
        var target = LifecycleReconciliation.Resolve(stored: null, user.Lifecycle, user.IsActive);
        if (target is { } state)
            ApplyLifecycle(user, state);
        return await base.CreateAsync(user).ConfigureAwait(false);
    }

    public override async Task<IdentityResult> UpdateAsync(TUser user)
    {
        var transitions = _services.GetService<IAccountTransitionStore>();
        var stored = transitions is null ? null : await transitions.GetStoredLifecycleAsync(user.Id).ConfigureAwait(false);
        var explicitChange = AccountLifecycleScope.Current;
        var target = LifecycleReconciliation.Resolve(stored, user.Lifecycle, user.IsActive, explicitChange?.RehireReviewed ?? false);
        if (target is not { } state)
            return IdentityResult.Failed(new IdentityError
            {
                Code = "IllegalLifecycleTransition",
                Description = $"An account cannot move from {stored} to {user.Lifecycle}.",
            });

        if (stored is { } from && from != state)
        {
            transitions!.Record(new ModulusAccountTransition
            {
                UserId = user.Id,
                FromState = from,
                ToState = state,
                ActorId = explicitChange is { } change ? change.ActorId : CurrentActor(),
                Reason = explicitChange is { } named ? named.Reason : state == AccountLifecycle.Disabled ? user.SuspensionReason : null,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            user.LifecycleChangedAt = DateTimeOffset.UtcNow;
        }

        ApplyLifecycle(user, state);
        var result = await base.UpdateAsync(user).ConfigureAwait(false);
        return !user.MayStartNewAccess()
            ? await NotifyOnSuccessAsync(result, user, AccessChangeKinds.Account, "account.disabled").ConfigureAwait(false)
            : result;
    }

    /// <summary>The lifecycle is saved with the legacy flag that agrees with it, so sign-in (which reads the flag) follows it.</summary>
    private static void ApplyLifecycle(TUser user, AccountLifecycle state)
    {
        user.Lifecycle = state;
        user.IsActive = AccountLifecycleRules.AllowsNewAccess(state);
    }

    private Guid? CurrentActor()
    {
        var current = _services.GetService<ICurrentUser>();
        return current is { IsAuthenticated: true, UserId: { } id } ? id : null;
    }

    private async Task<IdentityResult> NotifyOnSuccessAsync(IdentityResult result, TUser user, string kind, string reason)
    {
        if (!result.Succeeded)
            return result;

        var observers = _services.GetServices<IAccessChangeObserver>();
        await observers.NotifyAccessChangedAsync(
            new AccessChange { Kind = kind, Reason = reason, TenantId = user.TenantId, UserId = user.Id },
            _logger).ConfigureAwait(false);
        return result;
    }
}
