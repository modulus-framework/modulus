namespace Modulus.Settings;

/// <summary>
/// The version key of the cached setting values on this node. Every cached value is keyed by the version it was read under, so
/// <see cref="Advance"/> makes all of them stale at once, without walking the cache.
/// </summary>
public sealed class SettingCacheVersion
{
    private long _current;

    /// <summary>The version a read started under.</summary>
    public long Current => Interlocked.Read(ref _current);

    /// <summary>Makes every cached value stale.</summary>
    public void Advance() => Interlocked.Increment(ref _current);
}
