namespace Modulus.Core.Abstractions;

/// <summary>
/// Told when something that decides what a user may do has changed: a permission grant, a role, a company
/// membership, a delegation, an org placement, a feature entitlement, or an account being disabled. Systems that
/// cache a user's access outside this process subscribe to it (the AI connector tells the AI platform to drop its
/// cached access scopes). Register implementations in DI; every one is called.
/// </summary>
/// <remarks>
/// Called after the change is saved. An observer must not throw and should return quickly (queue the work);
/// <see cref="AccessChangeNotifications.NotifyAccessChangedAsync"/> logs and swallows a failure so the change itself
/// still succeeds.
/// </remarks>
public interface IAccessChangeObserver
{
    /// <summary>Handles one change.</summary>
    ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default);
}

/// <summary>One change to what users may do.</summary>
public sealed record AccessChange
{
    /// <summary>What changed, one of <see cref="AccessChangeKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>A short machine-readable reason, e.g. <c>grant.revoked</c>, <c>membership.removed</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The company the change applies to; null when it applies to the host or every company.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>The user the change applies to; null when it applies to many users (a role, a feature).</summary>
    public Guid? UserId { get; init; }

    /// <summary>When the change happened.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Built-in <see cref="AccessChange.Kind"/> values.</summary>
public static class AccessChangeKinds
{
    /// <summary>A permission granted, denied or revoked.</summary>
    public const string Grant = "grant";

    /// <summary>A user added to or removed from a role.</summary>
    public const string Role = "role";

    /// <summary>A company membership added or removed.</summary>
    public const string Membership = "membership";

    /// <summary>A company activated or deactivated.</summary>
    public const string Tenant = "tenant";

    /// <summary>A delegation created or revoked.</summary>
    public const string Delegation = "delegation";

    /// <summary>An org unit or a user's placement in one changed.</summary>
    public const string Organization = "organization";

    /// <summary>A feature entitlement changed.</summary>
    public const string Feature = "feature";

    /// <summary>An account disabled, locked or deleted.</summary>
    public const string Account = "account";
}

/// <summary>Calls every registered <see cref="IAccessChangeObserver"/>.</summary>
public static class AccessChangeNotifications
{
    /// <summary>
    /// Calls each observer in turn. A failing observer is logged (when <paramref name="logger"/> is given) and
    /// skipped, so a change that is already saved is never reported as failed.
    /// </summary>
    public static async ValueTask NotifyAccessChangedAsync(
        this IEnumerable<IAccessChangeObserver> observers,
        AccessChange change,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(change);

        foreach (var observer in observers)
        {
            try
            {
                await observer.OnAccessChangedAsync(change, ct).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // An observer failure must not fail a change that is already saved.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger?.LogError(ex, "Access change observer {Observer} failed for {Kind} {Reason}.",
                    observer.GetType().Name, change.Kind, change.Reason);
            }
        }
    }
}
