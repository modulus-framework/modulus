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
    /// <summary>
    /// The scope key of a value, as the table stores it. A branch value is keyed by its company and branch, so it needs no column of its
    /// own: the table's scope key carries it.
    /// </summary>
    public static string ScopeKey(Guid? tenantId, Guid? userId, Guid? branchId)
    {
        if (userId is { } user)
            return $"user:{tenantId?.ToString("N") ?? "none"}:{user:N}";
        if (branchId is { } branch)
            return $"branch:{tenantId?.ToString("N") ?? "none"}:{branch:N}";
        if (tenantId is { } tenant)
            return $"tenant:{tenant:N}";
        return "global";
    }

    public Task<string?> GetOrNullAsync(string name, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        var scope = ScopeKey(tenantId, userId, branchId);
        return db.SettingValues
            .AsNoTracking()
            .Where(s => s.Name == name && s.ScopeKey == scope)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetAsync(string name, string? value, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        if (value is null)
        {
            await RemoveAsync(name, tenantId, userId, branchId, ct).ConfigureAwait(false);
            return;
        }

        var scope = ScopeKey(tenantId, userId, branchId);
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
        await BumpVersionAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the change counter that the other nodes poll. It is written after the value, so a node that sees the new counter reads the
    /// new value. The first write creates the row; two first writes that race retry the update.
    /// </summary>
    private async Task BumpVersionAsync(CancellationToken ct)
    {
        var updated = await db.SettingVersions
            .Where(v => v.Name == SettingVersionEntity.SettingsName)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Version, v => v.Version + 1), ct)
            .ConfigureAwait(false);
        if (updated > 0)
            return;

        try
        {
            db.SettingVersions.Add(new SettingVersionEntity { Version = 1 });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another writer created the row first: count this change on that row.
            await db.SettingVersions
                .Where(v => v.Name == SettingVersionEntity.SettingsName)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Version, v => v.Version + 1), ct)
                .ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(string name, Guid? tenantId, Guid? userId, Guid? branchId, CancellationToken ct = default)
    {
        var scope = ScopeKey(tenantId, userId, branchId);
        await db.SettingValues
            .Where(s => s.Name == name && s.ScopeKey == scope)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        await BumpVersionAsync(ct).ConfigureAwait(false);
    }
}
