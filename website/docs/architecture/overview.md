---
sidebar_position: 1
---

# Architecture Overview

Modulus follows a **modular-monolith** architecture — a single deployable application composed of independent business modules with clear boundaries.

## Core Concepts

### Modular Monolith

```
┌──────────────────────────────────────────────────────────────┐
│                         Host Process                          │
│                                                               │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐             │
│  │  Module A   │  │  Module B   │  │  Module C   │             │
│  │  ─────────  │  │  ─────────  │  │  ─────────  │             │
│  │  Domain     │←─│  Domain     │  │  Domain     │             │
│  │  App        │  │  App        │←─│  App        │             │
│  │  Infra      │  │  Infra      │  │  Infra      │             │
│  │  Pres.      │  │  Pres.      │  │  Pres.      │             │
│  └────────────┘  └────────────┘  └────────────┘             │
│                                                               │
│  ┌──────────────────────────────────────────────────────────┐│
│  │              Shared Kernel (Framework)                    ││
│  │  Core · Data · Mediator · Events · Platform · Identity   ││
│  └──────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────┘
```

### Key Benefits

| Benefit | Description |
|---------|-------------|
| **Simplicity** | Single process, single deployment, single database transaction |
| **Performance** | In-process communication — no network serialization overhead |
| **Boundaries** | Each module owns its data and logic; cross-module calls go through well-defined interfaces |
| **Independent Development** | Teams can work on separate modules with minimal coordination |
| **Progressive Decomposition** | Extract modules to microservices when needed |

### How It Differs

| Architecture | Deployment | Communication | Data |
|-------------|------------|---------------|------|
| **Modular Monolith** | Single process | In-process | Per-module databases |
| **Microservices** | Multiple processes | Network (HTTP/gRPC) | Database per service |
| **Monolith** | Single process | Direct method calls | Shared database |

## Package Structure (34 libraries)

The framework ships 34 libraries under `src/` (plus the `Modulus.Cli` tool):

| Package | Purpose |
|---------|---------|
| `Modulus.Core` | Module system, DDD primitives, abstractions |
| `Modulus.AspNetCore` | ASP.NET Core integration, middleware, hardening |
| `Modulus.AspNetCore.Redis` | Distributed idempotency store |
| `Modulus.Data.Abstractions` | Repository and specification interfaces |
| `Modulus.EntityFrameworkCore` | EF Core integration, module DbContext |
| `Modulus.Data.{SqlServer,PostgreSQL,MySQL,SQLite}` | Database provider registrations |
| `Modulus.Data.MongoDB` | MongoDB document storage |
| `Modulus.Mediator` | CQRS mediator with pipeline behaviors |
| `Modulus.Events` | Domain events, integration events, in-process bus |
| `Modulus.Inbox` / `Modulus.Inbox.MongoDB` | Idempotent consumption (EF Core / MongoDB) |
| `Modulus.Outbox` | Transactional outbox processor |
| `Modulus.Outbox.Abstractions` | Outbox row factory (circular-dep seam) |
| `Modulus.Outbox.Management` | Dead-letter list/inspect/replay/purge API (EF) |
| `Modulus.Outbox.MongoDB` | MongoDB outbox + management API |
| `Modulus.EventBus.RabbitMQ` / `Modulus.EventBus.Kafka` | Message broker transports |
| `Modulus.Sagas` | Rebus-based saga orchestration |
| `Modulus.Identity` | OpenIddict + 6 external IdP adapters |
| `Modulus.Platform` | Multi-tenancy, authorization, jobs, caching, storage, SignalR |
| `Modulus.MultiTenancy.EntityFrameworkCore` | EF tenant store + per-tenant migration fan-out |
| `Modulus.Authorization.EntityFrameworkCore` | EF permission grant store |
| `Modulus.Authorization.Management` | Permission admin API |
| `Modulus.BackgroundJobs.Quartz` | Durable Quartz.NET scheduling |
| `Modulus.Caching.Redis` | Redis cache + invalidation backplane |
| `Modulus.Storage.S3` / `Modulus.Storage.AzureBlobs` | Cloud file storage (opt-in SDKs) |
| `Modulus.SignalR.Backplane` | Redis/Azure SignalR backplane (opt-in SDKs) |
| `Modulus.Observability` | OpenTelemetry bootstrap, tracing, health endpoints |
| `Modulus.Testing` / `Modulus.Testing.Architecture` | Test harness + module boundary rules |

## Solution Layout

```
src/
  core/          Modulus.Core, Modulus.AspNetCore (+ Redis idempotency store)
  data/          Abstractions, EF Core, providers (SqlServer, PostgreSQL, MySQL, SQLite, MongoDB)
  identity/      OpenIddict + 6 IdP adapters
  messaging/     Events, Mediator, Inbox (+MongoDB), Outbox (+Abstractions, Management, MongoDB), RabbitMQ, Kafka, Sagas
  platform/      Platform core + MultiTenancy.EFCore, Authorization.EFCore/Management,
                 BackgroundJobs.Quartz, Caching.Redis, Storage.S3/AzureBlobs, SignalR.Backplane
  observability/ OpenTelemetry wiring
  testing/       WebApplicationFactory harness + architecture rules
  cli/           Modulus.Cli scaffolding tool
tests/
  unit/          23 xUnit test projects
  integration/   Testcontainers-based tests
```
