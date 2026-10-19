using Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Adapter that wraps a Modulus IBackgroundJob&lt;TArgs&gt; as a Quartz.NET IJob.
/// Resolves the actual job from DI and executes it with the persisted arguments.
/// Restores the ambient tenant and correlation context from the JobDataMap
/// captured at enqueue time (mirrors ChannelJobQueue's pattern).
/// </summary>
public sealed class QuartzJobAdapter<TJob, TArgs> : IJob
    where TJob : IBackgroundJob<TArgs>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuartzJobAdapter<TJob, TArgs>> _logger;

    public QuartzJobAdapter(IServiceProvider serviceProvider, ILogger<QuartzJobAdapter<TJob, TArgs>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobDataMap = context.JobDetail.JobDataMap;
        var args = (TArgs)jobDataMap["args"]!;

        // ── Restore ambient context ────────────────────────────────
        // The tenant id is verified (ITenantContextRestorer): a job scheduled for a tenant that
        // has since been deactivated or deleted fails instead of running in an unchecked scope.
        Guid? tenantId = null;
        if (jobDataMap.ContainsKey("tenantId")
            && Guid.TryParse(jobDataMap.GetString("tenantId"), out var parsed))
        {
            tenantId = parsed;
        }

        var correlationId = jobDataMap.ContainsKey("correlationId")
            ? jobDataMap.GetString("correlationId") ?? string.Empty
            : string.Empty;

        using var tenantScope = tenantId is { } id
            ? _serviceProvider.EnterTenant(await _serviceProvider.VerifyTenantAsync(id, context.CancellationToken))
            : null;
        using var correlationScope = !string.IsNullOrEmpty(correlationId)
            && _serviceProvider.GetService<ICorrelationContext>() is { } correlation
                ? correlation.BeginScope(correlationId)
                : null;

        // The actor the job runs as (A6-2): visible to the job through JobContext and to every log line of this job.
        var actor = jobDataMap.GetString("actor") ?? JobActor.ForSystem(typeof(TJob).Name);
        if (_serviceProvider.GetService<JobContext>() is { } jobContext)
            jobContext.Actor = actor;
        using var actorScope = _logger.BeginScope(new Dictionary<string, object?> { ["JobActor"] = actor });

        // A job queued by a person runs only while that person may still start access (A6-1). A refused job is skipped, not retried.
        if (jobDataMap.GetString("initiatorId") is { Length: > 0 } initiator
            && Guid.TryParseExact(initiator, "N", out var initiatorId)
            && _serviceProvider.GetService<IJobInitiatorGuard>() is { } guard
            && !await guard.MayRunAsync(initiatorId, context.CancellationToken))
        {
            _logger.LogWarning("Job {Type} skipped: the account that queued it may no longer start access.", typeof(TJob).Name);
            _serviceProvider.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Authorization,
                Action = "job.refused",
                Outcome = SecurityAuditOutcomes.Denied,
                Actor = actor,
                Target = typeof(TJob).Name,
                TenantId = tenantId,
            });
            return;
        }

        var job = _serviceProvider.GetRequiredService<TJob>();
        try
        {
            await job.ExecuteAsync(args, context.CancellationToken);
        }
        catch (Exception ex)
        {
            // Let Quartz handle the exception according to its retry policy
            throw new JobExecutionException(ex);
        }
    }
}
