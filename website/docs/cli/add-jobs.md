# modulus add-jobs

Adds a durable background-job scheduler to the API host, backed by Quartz.

```bash
modulus add-jobs
```

## What it adds

- **The package reference** `Synvro.Modulus.BackgroundJobs.Quartz` in the API host.
- **The registration** in `Configuration/FeatureServicesSetup.cs`: `AddQuartzJobScheduler`.
- **The settings section** in `appsettings.json`.

Jobs scheduled with the framework's `IJobScheduler` are stored in Quartz's job store, so they survive a restart. The command is idempotent.
