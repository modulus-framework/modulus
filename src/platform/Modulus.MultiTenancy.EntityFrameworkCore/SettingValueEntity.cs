namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// One stored setting value. The key is <c>(Name, ScopeKey)</c>: <c>ScopeKey</c> is a single non-null text column, so every scope
/// is unique on every provider (a nullable column in a key would not be, on PostgreSQL, where NULLs never compare equal).
/// </summary>
public sealed class SettingValueEntity
{
    /// <summary>The setting's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The scope: <c>global</c>, <c>tenant:{tenantId}</c>, or <c>user:{tenantId or none}:{userId}</c>.</summary>
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>The company the value belongs to, or <see langword="null"/> for the installation.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The person the value belongs to, or <see langword="null"/> when it is not a personal value.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The value as text.</summary>
    public string Value { get; set; } = string.Empty;
}
