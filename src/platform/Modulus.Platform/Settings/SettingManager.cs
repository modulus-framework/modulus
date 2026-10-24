namespace Modulus.Settings;

using System.Globalization;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

/// <inheritdoc cref="ISettingManager" />
/// <param name="store">The raw value storage.</param>
/// <param name="definitions">The registered setting definitions.</param>
/// <param name="currentTenant">The company in scope.</param>
/// <param name="currentUser">The person in scope.</param>
/// <param name="options">The pins and the cache settings.</param>
/// <param name="security">The request's security context; its branch is the branch scope. Optional: without it no branch value applies.</param>
public sealed class SettingManager(
    ISettingStore store,
    ISettingDefinitionRegistry definitions,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IOptions<ModulusSettingsOptions> options,
    ISecurityContext? security = null) : ISettingManager
{
    private readonly ISettingStore _store = store;
    private readonly ISettingDefinitionRegistry _definitions = definitions;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ISecurityContext? _security = security;
    private readonly IReadOnlyDictionary<string, string> _pinned = options.Value.Pinned.AsReadOnly();

    public async Task<string?> GetOrNullAsync(string name, CancellationToken ct = default)
        => (await ResolveAsync(name, ct).ConfigureAwait(false)).Value;

    public async Task<SettingResolution> ResolveAsync(string name, CancellationToken ct = default)
    {
        // A host pin wins over every scope, so no narrower value can contradict it.
        if (_pinned.TryGetValue(name, out var pinned))
            return new SettingResolution(name, pinned, SettingSource.Pinned);

        // Narrowest scope first: user → branch → tenant → global → definition default.
        var tenantId = _currentTenant.TenantId;
        var userId = _currentUser.UserId;
        var branchId = _security?.BranchId;

        if (userId.HasValue)
        {
            var userValue = await _store.GetOrNullAsync(name, tenantId, userId, null, ct).ConfigureAwait(false);
            if (userValue is not null)
                return new SettingResolution(name, userValue, SettingSource.User);
        }

        // A branch is an organization unit of the company, so its value only applies with a company in scope.
        if (branchId.HasValue && tenantId.HasValue)
        {
            var branchValue = await _store.GetOrNullAsync(name, tenantId, null, branchId, ct).ConfigureAwait(false);
            if (branchValue is not null)
                return new SettingResolution(name, branchValue, SettingSource.Branch);
        }

        if (tenantId.HasValue)
        {
            var tenantValue = await _store.GetOrNullAsync(name, tenantId, null, null, ct).ConfigureAwait(false);
            if (tenantValue is not null)
                return new SettingResolution(name, tenantValue, SettingSource.Tenant);
        }

        var globalValue = await _store.GetOrNullAsync(name, null, null, null, ct).ConfigureAwait(false);
        if (globalValue is not null)
            return new SettingResolution(name, globalValue, SettingSource.Global);

        var fallback = _definitions.Find(name)?.DefaultValue;
        return new SettingResolution(name, fallback, fallback is null ? SettingSource.None : SettingSource.Default);
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
        RefuseIfPinned(name);
        if (value is not null)
            ValidateValue(name, value, scope);

        var (tenantId, userId, branchId) = ResolveScope(scope);
        return _store.SetAsync(name, value, tenantId, userId, branchId, ct);
    }

    public Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default)
    {
        RefuseIfPinned(name);
        var (tenantId, userId, branchId) = ResolveScope(scope);
        return _store.RemoveAsync(name, tenantId, userId, branchId, ct);
    }

    private void RefuseIfPinned(string name)
    {
        if (_pinned.ContainsKey(name))
            throw new ConflictException($"'{name}' is pinned by the host and cannot be changed.");
    }

    private void ValidateValue(string name, string value, SettingScope scope)
    {
        // The length limit holds for every name, registered or not.
        if (value.Length > SettingValueValidator.MaxValueLength)
            throw new ValidationException([$"'{name}' takes at most {SettingValueValidator.MaxValueLength} characters."]);

        var definition = _definitions.Find(name);
        if (definition is null)
            return;

        if (!definition.AllowedScopes.HasFlag(ToFlag(scope)))
            throw new ValidationException([$"'{name}' cannot be set at the {scope} scope."]);

        if (SettingValueValidator.Validate(definition, value) is { } problem)
            throw new ValidationException([problem]);
    }

    private static SettingScopes ToFlag(SettingScope scope) => scope switch
    {
        SettingScope.Global => SettingScopes.Global,
        SettingScope.Tenant => SettingScopes.Tenant,
        SettingScope.User => SettingScopes.User,
        SettingScope.Branch => SettingScopes.Branch,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown setting scope."),
    };

    // A company value written with no company in scope would land in the installation-wide row, so it is refused instead. The same holds
    // for a branch value with no branch in scope.
    private (Guid? TenantId, Guid? UserId, Guid? BranchId) ResolveScope(SettingScope scope) => scope switch
    {
        SettingScope.Global => (null, null, null),
        SettingScope.Tenant => (RequireTenant(scope), null, null),
        SettingScope.User => (_currentTenant.TenantId, _currentUser.UserId ?? throw new ValidationException(["A person-scoped setting needs a signed-in person."]), null),
        SettingScope.Branch => (RequireTenant(scope), null, _security?.BranchId ?? throw new ValidationException(["A branch-scoped setting needs a branch in scope."])),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown setting scope."),
    };

    private Guid RequireTenant(SettingScope scope)
        => _currentTenant.TenantId ?? throw new ValidationException([$"A {scope.ToString().ToLowerInvariant()}-scoped setting needs a company in scope."]);
}
