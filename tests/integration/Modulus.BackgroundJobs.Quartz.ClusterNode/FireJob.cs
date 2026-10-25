namespace Modulus.BackgroundJobs.Quartz.ClusterNode;

using global::Quartz;

/// <summary>Prints one line per execution, naming the run and the node that ran it. The parent test counts these lines.</summary>
public sealed class FireJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        var runId = context.MergedJobDataMap.GetString("runId");
        Console.WriteLine($"FIRED {runId} {context.Scheduler.SchedulerInstanceId}");
        return Task.CompletedTask;
    }
}
