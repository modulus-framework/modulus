---
sidebar_position: 4
---

# Background Jobs

Modulus ships an in-process background job system for **dev/test**,
plus a Quartz.NET package for production.

## In-Process Jobs (dev/test)

Jobs take typed args:

```csharp
public sealed class SendWelcomeEmailJob(IEmailService email)
    : IBackgroundJob<SendWelcomeEmailArgs>
{
    public async Task ExecuteAsync(SendWelcomeEmailArgs args, CancellationToken ct)
    {
        await email.SendAsync(args.Address, ct);
    }
}
```

### Scheduling Jobs

```csharp
// Run as soon as a worker is available
await _scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailArgs>(
    new(args.Address), ct);

// Run after a delay (held in memory as Task.Delay — lost on shutdown)
await _scheduler.ScheduleAsync<SendWelcomeEmailJob, SendWelcomeEmailArgs>(
    new(args.Address), TimeSpan.FromMinutes(5), ct);

// Cron schedule (in-memory; fires per replica — see below)
_scheduler.AddRecurring<DailyReportJob, DailyReportArgs>(
    "daily-report", "0 0 6 * * ?", new());
_scheduler.RemoveRecurring("daily-report");
```

### Job Queue

`ChannelJobQueue` uses `System.Threading.Channels` — bounded capacity **10,000**
(`FullMode.Wait`), `max(1, ProcessorCount/2)` workers, 30s recurring tick.
There is no options class.

### Ambient Context + Durability Boundary

- The envelope carries `TenantId`/`CorrelationId` at enqueue time and restores
  them on the worker — jobs see the same tenant/logs as the request that queued
  them.
- **Everything is in-memory**: delayed/recurring work is lost on shutdown, and
  recurring jobs fire **once per replica per tick**. Production logs a warning
  pointing at Quartz/Hangfire. For durable, clustered scheduling use the
  Quartz package (`Modulus.BackgroundJobs.Quartz`).

## See Also

- [Platform Overview](overview) — Other platform services
