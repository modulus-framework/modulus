using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.BackgroundJobs.Quartz;
using Modulus.BackgroundJobs.Quartz.ClusterNode;
using global::Quartz;

// Commands:
//   schedule <connection> <count> <fireAfterSeconds>  adds one-shot jobs; prints "SCHEDULED <runId>" for each.
//   run <connection> <nodeId> <seconds>               runs one clustered node for a while; prints "FIRED <runId> <nodeId>".
// The connection string names the PostgreSQL schema through its search_path.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: schedule <connection> <count> <fireAfterSeconds> | run <connection> <nodeId> <seconds>");
    return 2;
}

switch (args[0])
{
    case "schedule" when args.Length == 4:
        await ScheduleAsync(args[1], int.Parse(args[2]), int.Parse(args[3]));
        return 0;

    case "run" when args.Length == 4:
        await RunNodeAsync(args[1], args[2], int.Parse(args[3]));
        return 0;

    default:
        Console.Error.WriteLine($"unknown or malformed command: {args[0]}");
        return 2;
}

static async Task ScheduleAsync(string connection, int count, int fireAfterSeconds)
{
    await using var provider = BuildProvider(connection, clustered: false, nodeId: null);
    var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

    var fireAt = DateTimeOffset.UtcNow.AddSeconds(fireAfterSeconds);
    for (var i = 0; i < count; i++)
    {
        var runId = Guid.NewGuid().ToString("N");
        var job = JobBuilder.Create<FireJob>().WithIdentity("once-" + runId).UsingJobData("runId", runId).Build();
        var trigger = TriggerBuilder.Create()
            .StartAt(fireAt)
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .Build();
        await scheduler.ScheduleJob(job, trigger);
        Console.WriteLine($"SCHEDULED {runId}");
    }

    await scheduler.Shutdown();
}

static async Task RunNodeAsync(string connection, string nodeId, int seconds)
{
    await using var provider = BuildProvider(connection, clustered: true, nodeId: nodeId);
    var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
    await scheduler.Start();

    await Task.Delay(TimeSpan.FromSeconds(seconds));

    await scheduler.Shutdown(waitForJobsToComplete: true);
}

static ServiceProvider BuildProvider(string connection, bool clustered, string? nodeId)
{
    var services = new ServiceCollection();
    services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
    services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    services.AddQuartzJobScheduler(q => q.UseModulusPostgresStore(connection, clustered));

    // Each process is a separate node, so an explicit id keeps two nodes from claiming the same identity.
    if (nodeId is not null)
        services.Configure<QuartzOptions>(o => o["quartz.scheduler.instanceId"] = nodeId);

    return services.BuildServiceProvider();
}
