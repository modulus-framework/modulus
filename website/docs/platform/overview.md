---
sidebar_position: 1
---

# Platform Services Overview

`Modulus.Platform` provides cross-cutting concerns for multi-tenant applications.

## Included Services

| Service | Description |
|---------|-------------|
| **Multi-Tenancy** | Per-tenant data isolation and resolution |
| **Authorization** | Permission-based access control |
| **Background Jobs** | In-process job queue with Quartz option |
| **Caching** | In-memory cache with tag-based invalidation |
| **Storage** | File storage (local/S3/Azure) |
| **SignalR** | Real-time communication hub |

## Setup

```bash
modulus app MyApp  # Select features interactively
```

Or register individually:

```csharp
services.AddModulusMultiTenancy(config);
services.AddModulusAuthorization(config);
services.AddModulusBackgroundJobs(config);
services.AddModulusCaching(config);
services.AddModulusStorage(config);
services.AddModulusSignalR(config);
```

## Configuration

```json
{
  "MultiTenancy": {
    "Enabled": true,
    "Resolver": "header"
  },
  "Authorization": {
    "PermissionPolicy": "Modulus"
  },
  "BackgroundJobs": {
    "MaxConcurrentJobs": 5
  },
  "Caching": {
    "DefaultExpirationMinutes": 5
  }
}
```

## See Also

- [Multi-Tenancy](multi-tenancy) — Tenant resolution and isolation
- [Authorization](authorization) — Permission system
- [Background Jobs](background-jobs) — Job scheduling
- [Caching](caching) — Cache configuration
- [Storage](storage) — File storage
- [SignalR](signalr) — Real-time communication
