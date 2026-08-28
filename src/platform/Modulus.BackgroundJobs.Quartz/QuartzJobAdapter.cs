using Quartz;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Adapter that wraps a Modulus IBackgroundJob&lt;TArgs&gt; as a Quartz.NET IJob.
/// Resolves the actual job from DI and executes it with the persisted arguments.
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
