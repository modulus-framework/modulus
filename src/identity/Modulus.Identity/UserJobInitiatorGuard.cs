using Microsoft.AspNetCore.Identity;
using Modulus.BackgroundJobs;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// Lets a queued job run only while the account that queued it may still start access (A6-1): a disabled, suspended or archived
/// person's queued work is skipped. An account that no longer exists is refused too.
/// </summary>
internal sealed class UserJobInitiatorGuard<TUser>(UserManager<TUser> users) : IJobInitiatorGuard
    where TUser : ModulusUser
{
    public async Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct)
        => await users.FindByIdAsync(initiatorId.ToString()) is { } user && user.MayStartNewAccess();
}
