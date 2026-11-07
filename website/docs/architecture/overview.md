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

## Package Structure

The repository has 69 projects under `src/`, including analyzers, test helpers and the analytics sinks. The main packages are below (plus the `Synvro.Modulus.Cli` tool). Most libraries target `net8.0` and `net10.0`; the UI packages and the analyzer are the exceptions:

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
| `Modulus.Identity` | OpenIddict, ASP.NET Core Identity, 6 external IdP adapters, LDAP, SAML, passkeys, SCIM, sessions |
| `Modulus.Platform` | Multi-tenancy, authorization, jobs, caching, storage, SignalR |
| `Modulus.MultiTenancy.EntityFrameworkCore` | EF tenant store + per-tenant migration fan-out |
| `Modulus.Authorization.EntityFrameworkCore` | EF permission grant store |
| `Modulus.Authorization.Management` | Permission admin API |
| `Modulus.BackgroundJobs.Quartz` | Durable Quartz.NET scheduling |
| `Modulus.Caching.Redis` | Redis cache + invalidation backplane |
| `Modulus.Storage.S3` / `Modulus.Storage.AzureBlobs` | Cloud file storage (opt-in SDKs) |
| `Modulus.SignalR.Backplane` | Redis/Azure SignalR backplane (opt-in SDKs) |
| `Modulus.Observability` | OpenTelemetry bootstrap, tracing, health endpoints |
| `Modulus.Realtime` / `Modulus.Realtime.Redis` | Push integration events over SSE or SignalR (Redis backplane) |
| `Modulus.Webhooks` | Signed delivery of integration events to external endpoints |
| `Modulus.Bff` | Backends for frontends (web, mobile, partner) on YARP |
| `Modulus.Grpc` / `Modulus.GraphQL` | gRPC and GraphQL surfaces over the mediator |
| `Modulus.Media`, `Modulus.Localization.EntityFrameworkCore`, `Modulus.Notifications.EntityFrameworkCore` | Media, translation and notification stores |
| `Modulus.Secrets.OpenBao` | Secrets from OpenBao |
| `Modulus.Tenancy.Lifecycle` (+ `.EntityFrameworkCore`) | Tenant exports, legal holds, retention and deletion |
| `Modulus.AI.Connector` (+ `.EntityFrameworkCore`, `.Testing`) | Read-only connector for an external AI platform |
| `Modulus.Analytics` and its sinks, stores and catalogs | Versioned analytics datasets (see [Analytics](../analytics/overview)) |
| `Modulus.Testing` / `Modulus.Testing.Architecture` / `Modulus.Testing.Authorization` | Test harness, module boundary rules and authorization test helpers |

## Solution Layout

```
src/
  core/          Modulus.Core, Modulus.AspNetCore
  analyzers/     Modulus.Analyzers (MOD0001 to MOD0003, packed into Modulus.AspNetCore)
  data/          Abstractions, EF Core, providers (SqlServer, PostgreSQL, MySQL, SQLite), MongoDB
  identity/      Modulus.Identity (OpenIddict, IdP adapters), Modulus.Identity.IntegrationEvents
  messaging/     Events, Mediator, Inbox, Outbox, Outbox.Management, RabbitMQ, Kafka,
                 Sagas, Webhooks, Realtime (+ Redis)
  platform/      Modulus.Platform (multi-tenancy, authorization, jobs, caching, storage, SignalR) and
                 the add-on packages: Bff, Grpc, GraphQL, Caching.Redis, AuditLogging, Media,
                 Localization, Notifications, Secrets.OpenBao, Tenancy.Lifecycle, storage and SignalR backplanes
  analytics/     Analytics core, EF Core store, sinks (ClickHouse, Parquet, Doris, messaging),
                 catalogs (OpenMetadata, Apicurio), lineage (OpenLineage), DuckDB reconciliation
  ai/            AI platform connector, EF Core journal, conformance kit
  observability/ OpenTelemetry wiring
  testing/       Test harness, architecture rules, authorization test helpers
  cli/           Modulus.Cli scaffolding tool
tests/
  unit/          xUnit test projects (Category=Unit)
  integration/   Testcontainers-based tests (Category=Integration)
  benchmarks/    BenchmarkDotNet
```
