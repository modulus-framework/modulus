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
    /// <summary>How often the delegators' roles are re-read from the identity store. Default one minute.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Keeps each delegation's snapshot of the delegator's roles current. A delegation is capped by what its delegator holds,
/// and that cap is recomputed at every decision from the stored role names; if the delegator later loses a role, the
/// snapshot must follow or the delegate keeps authority nobody holds. A delegator the identity store no longer knows
/// keeps no roles (only their direct grants cap the delegation). Does nothing without an <see cref="IUserRoleDirectory"/>.
/// </summary>
public sealed class DelegationRoleRefresher(IServiceScopeFactory scopes, EfDelegationStore store, TimeProvider clock)
{
    /// <summary>Refreshes every live delegation (or only <paramref name="delegatorId"/>'s). Returns how many snapshots changed.</summary>
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
        var roles = new Dictionary<Guid, IReadOnlyCollection<string>>();
        foreach (var delegation in store.Live(clock.GetUtcNow()).Where(d => delegatorId is null || d.FromUserId == delegatorId))
        {
            if (!roles.TryGetValue(delegation.FromUserId, out var live))
            {
                live = await directory.GetRolesAsync(delegation.FromUserId, ct) ?? [];
                roles[delegation.FromUserId] = live;
            }

            if (!live.Order(StringComparer.Ordinal).SequenceEqual(delegation.FromRoles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
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
