namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Settings;

/// <summary>
/// Checks the settings change counter on a schedule and, when another node has changed a setting, empties this node's cache. Without
/// Redis this is how a change reaches the other nodes; the delay is <see cref="ModulusSettingsOptions.VersionCheckInterval"/>.
/// </summary>
internal sealed class SettingVersionPoller(
    IServiceScopeFactory scopes,
    SettingCacheVersion cache,
    IOptions<ModulusSettingsOptions> options,
    ILogger<SettingVersionPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.VersionCheckInterval;
        if (interval <= TimeSpan.Zero || options.Value.CacheDuration <= TimeSpan.Zero)
            return;

        // The first reading is the baseline: a node does not empty its cache at start-up for changes it never saw.
        long? last = null;
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var current = await ReadAsync(stoppingToken).ConfigureAwait(false);
                    if (last is { } previous && current != previous)
                        cache.Advance();
                    last = current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The database is unreachable: the cache keeps its entries until they expire, as before.
                    logger.LogDebug(ex, "Could not read the settings change counter");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<long> ReadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantStoreDbContextBase>();
        return await db.SettingVersions.AsNoTracking()
            .Where(v => v.Name == SettingVersionEntity.SettingsName)
            .Select(v => v.Version)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
