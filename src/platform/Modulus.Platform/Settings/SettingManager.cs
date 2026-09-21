namespace Modulus.Settings;

using System.Globalization;
using Modulus.Core.Abstractions;

/// <inheritdoc cref="ISettingManager" />
public sealed class SettingManager(
    ISettingStore store,
    ISettingDefinitionRegistry definitions,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ISettingManager
{
    private readonly ISettingStore _store = store;
    private readonly ISettingDefinitionRegistry _definitions = definitions;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<string?> GetOrNullAsync(string name, CancellationToken ct = default)
    {
        var tenantId = _currentTenant.TenantId;
        var userId = _currentUser.UserId;

        // Narrowest scope first: user → tenant → global → definition default.
        if (userId.HasValue)
        {
            var userValue = await _store.GetOrNullAsync(name, tenantId, userId, ct).ConfigureAwait(false);
            if (userValue is not null)
                return userValue;
        }

        if (tenantId.HasValue)
        {
            var tenantValue = await _store.GetOrNullAsync(name, tenantId, null, ct).ConfigureAwait(false);
            if (tenantValue is not null)
                return tenantValue;
        }

        var globalValue = await _store.GetOrNullAsync(name, null, null, ct).ConfigureAwait(false);
        return globalValue ?? _definitions.Find(name)?.DefaultValue;
    }

    public async Task<T?> GetAsync<T>(string name, T? fallback = default, CancellationToken ct = default)
    {
        var raw = await GetOrNullAsync(name, ct).ConfigureAwait(false);
        if (raw is null)
            return fallback;

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            if (target == typeof(string))
                return (T?)(object)raw;
            if (target.IsEnum)
                return (T?)Enum.Parse(target, raw, ignoreCase: true);
            if (target == typeof(Guid))
                return (T?)(object)Guid.Parse(raw);
            if (target == typeof(TimeSpan))
                return (T?)(object)TimeSpan.Parse(raw, CultureInfo.InvariantCulture);

            return (T?)Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return fallback;
        }
    }

    public Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default)
    {
        var (tenantId, userId) = ResolveScope(scope);
        return _store.SetAsync(name, value, tenantId, userId, ct);
    }

    public Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default)
    {
        var (tenantId, userId) = ResolveScope(scope);
        return _store.RemoveAsync(name, tenantId, userId, ct);
    }

    private (Guid? TenantId, Guid? UserId) ResolveScope(SettingScope scope) => scope switch
    {
        SettingScope.Global => (null, null),
        SettingScope.Tenant => (_currentTenant.TenantId, null),
        SettingScope.User => (_currentTenant.TenantId, _currentUser.UserId),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown setting scope."),
    };
}
