using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Governance;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>Settings for <see cref="DelegationRoleRefreshService"/> (<c>Authorization:Delegation</c>).</summary>
public sealed class DelegationRoleRefreshOptions
{
    /// <summary>How often the delegators' roles are re-read from the identity store. Default 30 seconds, so a role change the identity store does not report reaches a delegation's cap well inside the 60-second revocation target.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Keeps each delegation in step with its delegator (A9-2). A delegation is capped by what its delegator holds, and that cap is
/// recomputed at every decision from the stored role names, so the snapshot must follow the delegator's roles. A delegator who may
/// no longer start access (disabled, locked out, archived, or unknown to the identity store) has their delegations <b>ended</b>, not
/// just capped: a delegation outlives its delegator's own access only through a snapshot nobody refreshed. The delegation resolver
/// stays synchronous, so the store is the decision-time gate: the access-change observer ends the delegations at once, and the timer
/// bounds staleness for a change that was not reported. Does nothing without an <see cref="IUserRoleDirectory"/>.
/// </summary>
public sealed class DelegationRoleRefresher(IServiceScopeFactory scopes, EfDelegationStore store, TimeProvider clock)
{
    /// <summary>Refreshes every live delegation (or only <paramref name="delegatorId"/>'s). Returns how many delegations changed (refreshed or ended).</summary>
    public async Task<int> RefreshAsync(Guid? delegatorId = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetService<IUserRoleDirectory>();
        if (directory is null)
            return 0;

        // The identity store is tenant-filtered; delegators are resolved from the host, as the management API does.
        var tenant = scope.ServiceProvider.GetService<ICurrentTenant>();
        using var host = tenant?.Change(null);

        var changed = 0;
        var delegators = new Dictionary<Guid, IReadOnlyCollection<string>?>();
        foreach (var delegation in store.Live(clock.GetUtcNow()).Where(d => delegatorId is null || d.FromUserId == delegatorId))
        {
            if (!delegators.TryGetValue(delegation.FromUserId, out var live))
            {
                live = await directory.MayStartAccessAsync(delegation.FromUserId, ct)
                    ? await directory.GetRolesAsync(delegation.FromUserId, ct) ?? []
                    : null;
                delegators[delegation.FromUserId] = live;
            }

            if (live is null)
            {
                await store.RevokeAsync(delegation.Id, ct);
                changed++;
            }
            else if (!live.Order(StringComparer.Ordinal).SequenceEqual(delegation.FromRoles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                await store.SetFromRolesAsync(delegation.Id, live, ct);
                changed++;
            }
        }

        return changed;
    }
}

/// <summary>Runs <see cref="DelegationRoleRefresher"/> on a timer.</summary>
public sealed class DelegationRoleRefreshService(
    DelegationRoleRefresher refresher,
    IOptions<DelegationRoleRefreshOptions> options,
    ILogger<DelegationRoleRefreshService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await refresher.RefreshAsync(ct: stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Delegation role refresh failed; the previous snapshots stay in force until the next run.");
            }
        }
    }
}

/// <summary>Refreshes a user's delegations at once when their access changes, instead of waiting for the next timer tick.</summary>
public sealed class DelegationRoleRefreshObserver(DelegationRoleRefresher refresher) : IAccessChangeObserver
{
    /// <inheritdoc />
    public async ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.UserId is { } user)
            await refresher.RefreshAsync(user, ct).ConfigureAwait(false);
    }
}
