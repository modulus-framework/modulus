namespace Modulus.Settings;

using System.Collections.Concurrent;

/// <summary>
/// Dependency-free <see cref="ISettingStore"/> default. Single-node only:
/// multi-node deployments replace this with a distributed (Redis/EF) store
/// registered <i>before</i> <c>AddModulusSettings</c> (<c>TryAdd</c> keeps it).
/// </summary>
public sealed class InMemorySettingStore : ISettingStore
{
    private readonly ConcurrentDictionary<(string Name, Guid? TenantId, Guid? UserId), string> _values = new();

    public Task<string?> GetOrNullAsync(
        string name,
        Guid? tenantId,
        Guid? userId,
        CancellationToken ct = default)
        => Task.FromResult(
            _values.TryGetValue((name, tenantId, userId), out var value) ? value : null);

    public Task SetAsync(
        string name,
        string? value,
        Guid? tenantId,
        Guid? userId,
        CancellationToken ct = default)
    {
        if (value is null)
            _values.TryRemove((name, tenantId, userId), out _);
        else
            _values[(name, tenantId, userId)] = value;

        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string name,
        Guid? tenantId,
        Guid? userId,
        CancellationToken ct = default)
    {
        _values.TryRemove((name, tenantId, userId), out _);
        return Task.CompletedTask;
    }
}
