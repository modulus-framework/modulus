namespace Modulus.Settings;

/// <summary>
/// Effective-value setting API. Reads resolve user → tenant → global →
/// definition default; writes target one explicit <see cref="SettingScope"/>
/// using the ambient tenant/user. Authorization is enforced by callers
/// (management endpoints), not here.
/// </summary>
public interface ISettingManager
{
    Task<string?> GetOrNullAsync(string name, CancellationToken ct = default);

    Task<T?> GetAsync<T>(string name, T? fallback = default, CancellationToken ct = default);

    Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default);

    Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default);
}
