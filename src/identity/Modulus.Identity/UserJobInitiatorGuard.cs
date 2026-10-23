using Microsoft.AspNetCore.Identity;
using Modulus.Authorization.Grants;
using Modulus.BackgroundJobs;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// Lets a queued job run only while the account that queued it may still start access (A6-1): a disabled, suspended or archived
/// person's queued work is skipped. An account that no longer exists is refused too. A step that needs a permission is allowed
/// only while the account still holds it through the grant store; with no permission resolver registered, such a step is refused
/// (fail closed).
/// </summary>
internal sealed class UserJobInitiatorGuard<TUser>(UserManager<TUser> users, IPermissionResolver? resolver = null) : IJobInitiatorGuard
    where TUser : ModulusUser
{
    public async Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct)
        => await users.FindByIdAsync(initiatorId.ToString()) is { } user && user.MayStartNewAccess();

    public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
        => MayPerformCoreAsync(initiatorId, agentRole: null, permission);

    // The agent's cap (A9-1): the person's permission is intersected with the agent role's, by the resolver, through AgentRole.
    public Task<bool> MayPerformAsAgentAsync(Guid initiatorId, string agentRole, string permission, CancellationToken ct)
        => MayPerformCoreAsync(initiatorId, agentRole, permission);

    private async Task<bool> MayPerformCoreAsync(Guid initiatorId, string? agentRole, string permission)
    {
        if (resolver is null)
            return false;
        if (await users.FindByIdAsync(initiatorId.ToString()) is not { } user || !user.MayStartNewAccess())
            return false;

        var roles = await users.GetRolesAsync(user);
        return resolver.Resolve(new PrincipalGrantQuery(user.Id, roles.ToArray(), agentRole)).Contains(permission);
    }
}
