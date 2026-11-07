---
sidebar_position: 1
---

# Platform Services Overview

`Modulus.Platform` holds the cross-cutting services a multi-tenant application needs. The core package has no cloud SDKs. Each cloud or external provider is a separate package, so you only pull in the SDKs you use.

## Included services

| Service | Package | Page |
|---------|---------|------|
| Multi-tenancy (resolution, membership, tenant store) | `Modulus.Platform` (store in `Modulus.MultiTenancy.EntityFrameworkCore`) | [Multi-Tenancy](./multi-tenancy) |
| Authorization (permissions, grants, scopes, approvals) | `Modulus.Platform`, `Modulus.Authorization.EntityFrameworkCore` | [Authorization](./authorization) |
| Background jobs (in-process, or Quartz for durable clusters) | `Modulus.Platform`, `Modulus.BackgroundJobs.Quartz` | [Background Jobs](./background-jobs) |
| Caching (memory, FusionCache hybrid, Redis) | `Modulus.Platform`, `Modulus.Caching.Redis` | [Caching](./caching) |
| File storage (local, S3, Azure Blobs) | `Modulus.Platform`, `Modulus.Storage.S3`, `Modulus.Storage.AzureBlobs` | [Storage](./storage) |
| Real-time hub (SignalR) | `Modulus.Platform`, `Modulus.SignalR.Backplane` | [SignalR](./signalr) |
| Realtime push (SSE and SignalR) | `Modulus.Realtime` | [Realtime](./realtime) |
| Backends for frontends | `Modulus.Bff` | [BFF](./bff) |
| gRPC and GraphQL | `Modulus.Grpc`, `Modulus.GraphQL` | [gRPC](./grpc), [GraphQL](./graphql) |
| Media, localization, notifications, secrets | see [Platform add-ons](./add-ons) | [Platform add-ons](./add-ons) |

## Registration

Register the services you need in the host's `Program.cs`. The sections below show the main entry points. Each page has the full options.

```csharp
builder.Services.AddMultiTenancy(t => t
    .UseJwtClaimResolver()
    .UseHeaderResolver()
    .RequireMembership());

builder.Services.AddModulusAuthorization();
builder.Services.AddBackgroundJobs(typeof(Program).Assembly);
builder.Services.AddModulusCaching();
builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddModulusSignalR(builder.Configuration);
```

`AddModulusFusionCache(configuration)` replaces the memory cache with a hybrid cache when you need stampede protection or a Redis layer.

## Configuration

Each service binds its own section. These are the sections the platform reads:

| Section | Used by | Example |
|---------|---------|---------|
| `Storage` | `AddFileStorage` | `"Storage": { "Provider": "Local", "BasePath": "./uploads" }` |
| `Caching:Fusion` | `AddModulusFusionCache` | `"Caching": { "Fusion": { "CacheName": "catalog" } }` |
| `Caching:Redis:ConnectionString` | `AddRedisCacheService` | Redis connection string |
| `Localization` | Localization options | Culture settings |
| `Secrets` | `AddModulusSecrets` | Secret store settings |
| `Security:Audit` | `AddModulusSecurityAudit` | Audit spool and anchor files |

## See also

- [Multi-Tenancy](./multi-tenancy): tenant resolution and isolation
- [Security Model](../security/overview): how tenancy, permissions and audit fit together
- [Platform add-ons](./add-ons): media, localization, notifications, secrets and tenant lifecycle
