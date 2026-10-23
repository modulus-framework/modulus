namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Modulus.Settings;

/// <summary>
/// The durable <see cref="ISettingStore"/>: values live in <c>ModulusSettingValues</c> in the tenant store database. Every read and
/// write names its scope explicitly, so the store is tenant-isolated even when no company is in scope.
/// </summary>
/// <remarks>
/// Two writes to the same setting at the same scope at once can both insert; one fails on the key and the caller sees the
/// exception. Settings change rarely, and a retry repeats the write, so no lock is taken.
/// </remarks>
public sealed class EfSettingStore(TenantStoreDbContextBase db) : ISettingStore
{
    /// <summary>The scope key of a value, as the table stores it.</summary>
    public static string ScopeKey(Guid? tenantId, Guid? userId)
    {
        if (userId is { } user)
            return $"user:{tenantId?.ToString("N") ?? "none"}:{user:N}";
        if (tenantId is { } tenant)
            return $"tenant:{tenant:N}";
        return "global";
    }

    public Task<string?> GetOrNullAsync(string name, Guid? tenantId, Guid? userId, CancellationToken ct = default)
    {
        var scope = ScopeKey(tenantId, userId);
        return db.SettingValues
            .AsNoTracking()
            .Where(s => s.Name == name && s.ScopeKey == scope)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetAsync(string name, string? value, Guid? tenantId, Guid? userId, CancellationToken ct = default)
    {
        if (value is null)
        {
            await RemoveAsync(name, tenantId, userId, ct).ConfigureAwait(false);
            return;
        }

        var scope = ScopeKey(tenantId, userId);
        var row = await db.SettingValues.FirstOrDefaultAsync(s => s.Name == name && s.ScopeKey == scope, ct).ConfigureAwait(false);
        if (row is null)
        {
            db.SettingValues.Add(new SettingValueEntity { Name = name, ScopeKey = scope, TenantId = tenantId, UserId = userId, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string name, Guid? tenantId, Guid? userId, CancellationToken ct = default)
    {
        var scope = ScopeKey(tenantId, userId);
        await db.SettingValues
            .Where(s => s.Name == name && s.ScopeKey == scope)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
