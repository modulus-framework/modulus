using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

namespace Modulus.AuditLogging.Security.Retention;

/// <summary>
/// Runs <see cref="SecurityAuditRetention"/> on a schedule (A11-1). Retention purges only what the archive holds, so with no archive it logs
/// that it kept every entry. When a distributed lock is registered, one replica runs a pass at a time, so a chain is never archived twice.
/// The purge is also safe to repeat: the database and the checkpoint refuse anything that is not a clean prefix of the chain.
/// </summary>
public sealed class SecurityAuditRetentionService(
    SecurityAuditRetention retention,
    IServiceProvider services,
    IOptions<SecurityAuditRetentionOptions> options,
    TimeProvider clock,
    ILogger<SecurityAuditRetentionService> logger) : BackgroundService
{
    /// <summary>The cluster-wide lock one replica holds while it runs a pass.</summary>
    public const string LockName = "modulus:audit:retention:leader";

    /// <summary>
    /// Runs one pass over every chain, under the lock when one is registered. Returns the per-chain results; empty when another replica holds
    /// the lock.
    /// </summary>
    public async Task<IReadOnlyList<SecurityAuditRetentionResult>> RunOnceAsync(CancellationToken ct = default)
    {
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        if (lease is null && services.GetService<IDistributedLock>() is not null)
        {
            logger.LogDebug("Security audit retention skipped: another replica holds the lock.");
            return [];
        }

        var results = await retention.RunAsync(ct).ConfigureAwait(false);
        foreach (var group in results.GroupBy(r => r.Outcome))
            logger.LogInformation("Security audit retention: {Outcome} on {Chains} chain(s).", group.Key, group.Count());
        var purged = results.Where(r => r.Outcome == SecurityAuditRetentionOutcome.Purged).Sum(r => r.Purged);
        if (purged > 0)
            logger.LogInformation("Security audit retention purged {Entries} entries.", purged);
        return results;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        if (!options.Value.Enabled || interval <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed pass is retried on the next tick; the chains it did not reach are unchanged.
                    logger.LogError(ex, "Security audit retention pass failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<IAsyncDisposable?> AcquireAsync(CancellationToken ct)
    {
        var locks = services.GetService<IDistributedLock>();
        return locks is null
            ? null
            : await locks.TryAcquireAsync(LockName, options.Value.LockLease, ct).ConfigureAwait(false);
    }
}

public static class SecurityAuditRetentionExtensions
{
    /// <summary>
    /// Opts the application into scheduled security audit retention (A11-1): binds <c>Security:Retention</c>, registers the retention pass and
    /// its hosted schedule, and keeps the archive refusal default (<see cref="NoSecurityAuditArchive"/>) unless the application registers an
    /// archive. The hold register and the store come from <c>AddModulusAuditStore</c> / <c>AddModulusSecurityAudit</c>. Opt-in because retention
    /// deletes data: nothing runs on a schedule until this is called.
    /// </summary>
    public static IServiceCollection AddModulusSecurityAuditRetention(
        this IServiceCollection services, IConfiguration? configuration = null, Action<SecurityAuditRetentionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<SecurityAuditRetentionOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(SecurityAuditRetentionOptions.SectionName));
        if (configure is not null)
            options.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecurityAuditArchive, NoSecurityAuditArchive>();
        services.TryAddSingleton<ISecurityAuditHolds, SecurityAuditHolds>();
        services.TryAddSingleton<SecurityAuditRetention>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecurityAuditRetentionService>());
        return services;
    }
}
