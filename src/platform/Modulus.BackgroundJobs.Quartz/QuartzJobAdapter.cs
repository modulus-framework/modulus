using Quartz;
using Microsoft.Extensions.DependencyInjection;
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

    public QuartzJobAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var job = _serviceProvider.GetRequiredService<TJob>();
        var args = (TArgs)context.JobDetail.JobDataMap["args"]!;
        var jobDataMap = context.JobDetail.JobDataMap;

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
