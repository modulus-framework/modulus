---
sidebar_position: 4
---

# Background Jobs

Modulus provides an in-process background job system with optional Quartz.NET integration.

## Setup

```csharp
services.AddModulusBackgroundJobs(config);
```

## Defining a Job

```csharp
public sealed class SendWelcomeEmailJob : IBackgroundJob
{
    private readonly IEmailService _email;

    public SendWelcomeEmailJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // Job logic here
    }
}
```

## Scheduling Jobs

### One-Time Jobs

```csharp
public sealed class RegisterUserHandler : ICommandHandler<RegisterUser, Unit>
{
    private readonly IJobScheduler _scheduler;

    public async Task<Unit> HandleAsync(RegisterUser command, CancellationToken ct)
    {
        // ... register user

        await _scheduler.EnqueueAsync<SendWelcomeEmailJob>(
            TimeSpan.FromMinutes(5)); // Delay 5 minutes

        return Unit.Value;
    }
}
```

### Recurring Jobs

```csharp
await _scheduler.ScheduleAsync<DailyReportJob>(
    CronExpression.Daily);
```

## Job Queue

The default `ChannelJobQueue` uses `System.Threading.Channels`:

```json
{
  "BackgroundJobs": {
    "MaxConcurrentJobs": 5,
    "QueueCapacity": 1000
  }
}
```

## Quartz Integration

For production-grade scheduling:

```bash
modulus app MyApp --scheduler quartz
```

```csharp
services.AddModulusBackgroundJobs(config)
    .UseQuartz(config);
```

Quartz provides:

- Persistent job storage
- Cluster support
- Cron expressions
- Misfire handling

## See Also

- [Platform Overview](overview) — Other platform services
