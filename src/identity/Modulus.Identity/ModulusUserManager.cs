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
        => await NotifyOnSuccessAsync(await base.AddToRoleAsync(user, role).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.added")
            .ConfigureAwait(false);

    public override async Task<IdentityResult> AddToRolesAsync(TUser user, IEnumerable<string> roles)
        => await NotifyOnSuccessAsync(await base.AddToRolesAsync(user, roles).ConfigureAwait(false), user, AccessChangeKinds.Role, "role.added")
            .ConfigureAwait(false);

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

    public override async Task<IdentityResult> UpdateAsync(TUser user)
    {
        var result = await base.UpdateAsync(user).ConfigureAwait(false);
        return user is { IsActive: false }
            ? await NotifyOnSuccessAsync(result, user, AccessChangeKinds.Account, "account.disabled").ConfigureAwait(false)
            : result;
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
