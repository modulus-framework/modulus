namespace Modulus.AI.Connector.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.ChangeHistory;
using Modulus.EntityFrameworkCore.ModelBuilding;
using Modulus.EntityFrameworkCore.Saving;
using Modulus.Mediator.Abstractions;

/// <summary>How long the <c>ai_changes</c> journal keeps rows.</summary>
public sealed class AiChangeJournalOptions
{
    /// <summary>
    /// Rows older than this are purged (default 30 days). The platform must read <c>/changes</c> more often than this,
    /// or it has to re-extract.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the purge runs (default hourly); zero or negative turns it off.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>EF Core data access for the AI connector.</summary>
public static class AiConnectorEfExtensions
{
    /// <summary>
    /// Reads and journals the app's entities through its module contexts: the <c>ai_changes</c> journal (mapped into every
    /// <c>ModuleDbContext</c>, so add a migration, and written in the same transaction as each
    /// <see cref="Core.Abstractions.Ai.AiIndexedAttribute"/> entity), the change feed behind <c>/changes</c>, the entity
    /// source behind <c>/extract</c> and the generated <c>Search</c>/<c>Calculate</c> capabilities, and the journal purge.
    /// </summary>
    public static AiConnectorBuilder UseEntityFrameworkCore(this AiConnectorBuilder builder, Action<AiChangeJournalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<AiChangeJournalOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleModelContributor, AiChangeModelContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleSaveContributor, AiChangeSaveContributor>());
        services.TryAddScoped<IAiChangeFeed, EfAiChangeFeed>();
        services.TryAddScoped<IAiEntitySource, EfAiEntitySource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AiChangeJournalPurgeService>());
        return builder;
    }

    /// <summary>
    /// Exposes <see cref="ListEntityChanges"/> (<c>Modulus.Audit.EntityChange.List</c>, <c>audit:view</c>) over the
    /// entity change history. Needs <see cref="UseEntityFrameworkCore"/> and <c>AddEntityChangeHistory()</c>.
    /// </summary>
    public static AiConnectorBuilder AddEntityChangeHistoryCapability(this AiConnectorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<IQueryHandler<ListEntityChanges, IReadOnlyList<EntityChangeRecord>>, ListEntityChangesHandler>();
        return builder.AddCapability<ListEntityChanges>();
    }
}

/// <summary>Deletes journal rows older than <see cref="AiChangeJournalOptions.Retention"/> from every module context.</summary>
internal sealed class AiChangeJournalPurgeService(
    IServiceScopeFactory scopes,
    IOptions<AiChangeJournalOptions> options,
    TimeProvider time,
    ILogger<AiChangeJournalPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.PurgeInterval <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(options.Value.PurgeInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI connector: purging the change journal failed.");
            }
        }
    }

    /// <summary>Deletes the expired rows; returns how many.</summary>
    internal async Task<int> PurgeAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        // The journal spans every company: purge in the host context.
        using var host = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(null);
        var cutoff = (time.GetUtcNow() - options.Value.Retention).UtcDateTime;
        var deleted = 0;
        foreach (var context in scope.ServiceProvider.GetServices<DbContext>().DistinctBy(c => c.GetType()))
        {
            if (context.Model.FindEntityType(typeof(AiChangeRecord)) is not null)
                deleted += await context.Set<AiChangeRecord>().Where(c => c.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        }

        return deleted;
    }
}
