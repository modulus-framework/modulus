namespace Modulus.Settings;

/// <summary>
/// Tells the other nodes that a setting value changed, so each one makes its cached values stale. The default does nothing,
/// which is right for one node. A multi-node deployment registers a shared-channel notifier (<c>AddRedisSettingCacheBackplane</c>).
/// </summary>
/// <remarks>
/// Notification is best effort: a message that is lost leaves the other nodes stale until their cache duration ends, so the
/// duration is the bound on staleness. An implementation must not throw for a delivery failure, because the write has already
/// been committed.
/// </remarks>
public interface ISettingChangeNotifier
{
    /// <summary>Announces a committed change to the other nodes.</summary>
    Task NotifyChangedAsync(CancellationToken ct = default);
}

/// <summary>A notifier that tells nobody: the single-node default.</summary>
public sealed class NullSettingChangeNotifier : ISettingChangeNotifier
{
    /// <inheritdoc />
    public Task NotifyChangedAsync(CancellationToken ct = default) => Task.CompletedTask;
}
