using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>One sign-in event of an account.</summary>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Action">What: <c>signin.password</c> (a refused sign-in), <c>token.password</c>, <c>token.refresh</c>, <c>2fa.enabled</c>, ...</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Reason">Why it was refused (<c>wrong-password</c>, <c>locked-out</c>, <c>mfa-required</c>, ...), when it was.</param>
/// <param name="ClientId">The application that asked, when recorded.</param>
public sealed record LoginEvent(DateTimeOffset OccurredAt, string Action, bool Succeeded, string? Reason, string? ClientId);

/// <summary>
/// An account's recent sign-ins and refused attempts, read from the security audit (the hash-chained record), so a user can spot a
/// stranger and an administrator can investigate. Empty when no durable or in-memory audit store is registered.
/// </summary>
public interface ILoginHistoryService
{
    /// <summary>The newest events of <paramref name="userId"/> first, at most <paramref name="take"/> (1 to 200).</summary>
    Task<IReadOnlyList<LoginEvent>> GetAsync(Guid userId, int take = 50, CancellationToken ct = default);
}

internal sealed class LoginHistoryService<TUser>(UserManager<TUser> users, IServiceProvider services) : ILoginHistoryService
    where TUser : ModulusUser
{
    public async Task<IReadOnlyList<LoginEvent>> GetAsync(Guid userId, int take = 50, CancellationToken ct = default)
    {
        if (services.GetService<ISecurityAuditStore>() is not { } store || await users.FindByIdAsync(userId.ToString()) is not { } user)
            return [];

        // Sign-in events sit in the account's company chain (the host chain for a host-level account).
        var chain = SecurityAuditChain.ChainOf(user.TenantId);
        var identity = await store.QueryAsync(
            new SecurityAuditQuery(chain, SecurityAuditCategories.Identity, userId.ToString(), Take: Math.Clamp(take, 1, 200)), ct);
        return [.. identity.Select(r => new LoginEvent(
            r.OccurredAt, r.Action, r.Outcome is SecurityAuditOutcomes.Success,
            r.Details.TryGetValue("reason", out var reason) ? reason : null,
            r.Details.TryGetValue("client", out var client) ? client : null))];
    }
}
