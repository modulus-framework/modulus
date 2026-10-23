namespace Modulus.Settings;

/// <summary>
/// Effective-value setting API. Reads resolve pin → user → tenant → global → definition default; writes target one explicit
/// <see cref="SettingScope"/> using the ambient tenant/user. Authorization is enforced by callers (management endpoints), not here.
/// </summary>
public interface ISettingManager
{
    Task<string?> GetOrNullAsync(string name, CancellationToken ct = default);

    /// <summary>The effective value and the scope it came from.</summary>
    Task<SettingResolution> ResolveAsync(string name, CancellationToken ct = default);

    Task<T?> GetAsync<T>(string name, T? fallback = default, CancellationToken ct = default);

    /// <summary>Stores a value at <paramref name="scope"/>. Refused (409) for a pinned name; validated (400) against a registered definition.</summary>
    Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default);

    Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default);
}
