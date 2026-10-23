namespace Modulus.Settings;

/// <summary>Configuration of the setting system, bound from the <c>Settings</c> section.</summary>
public sealed class ModulusSettingsOptions
{
    /// <summary>
    /// Values the host fixes (<c>Settings:Pinned:{name}</c>). A pinned value is returned for every caller and every scope, and no
    /// scope can change it: writes are refused with 409. Pins come from configuration, so they are reviewed like any other deployment setting.
    /// </summary>
    public Dictionary<string, string> Pinned { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long the database store keeps a value on this node before it reads the database again (<c>Settings:CacheDuration</c>). Zero
    /// turns the cache off. A write on this node invalidates the cache at once; another node sees it within this duration.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a database-backed store checks, on this node, whether another node changed a value (<c>Settings:VersionCheckInterval</c>).
    /// A change seen this way empties the node's cache. It is the bound on how long another node's change waits when Redis is not
    /// registered (<c>AddRedisSettingCacheBackplane</c> is faster). Zero turns the check off.
    /// </summary>
    public TimeSpan VersionCheckInterval { get; set; } = TimeSpan.FromSeconds(5);
}
