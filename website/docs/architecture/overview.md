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

## Package Structure (23 packages)

The framework was consolidated from 55 to 23 packages:

| Package | Purpose |
|---------|---------|
| `Modulus.Core` | Module system, DDD primitives, abstractions |
| `Modulus.AspNetCore` | ASP.NET Core integration, middleware, hardening |
| `Modulus.Data.Abstractions` | Repository and specification interfaces |
| `Modulus.EntityFrameworkCore` | EF Core integration, module DbContext |
| `Modulus.Data.{SqlServer,PostgreSQL,MySQL,SQLite}` | Database provider registrations |
| `Modulus.Data.MongoDB` | MongoDB document storage |
| `Modulus.Mediator` | CQRS mediator with pipeline behaviors |
| `Modulus.Events` | Domain events, integration events, in-process bus |
| `Modulus.Inbox` | Idempotent message consumption (EF Core) |
| `Modulus.Outbox` | Transactional outbox processor |
| `Modulus.Outbox.Abstractions` | Outbox row factory (circular-dep seam) |
| `Modulus.Inbox.MongoDB` / `Modulus.Outbox.MongoDB` | MongoDB variants |
| `Modulus.EventBus.RabbitMQ` / `Modulus.EventBus.Kafka` | Message broker transports |
| `Modulus.Sagas` | Rebus-based saga orchestration |
| `Modulus.Identity` | OpenIddict + external IdP adapters |
| `Modulus.Platform` | Multi-tenancy, authorization, caching, storage, SignalR |
| `Modulus.Observability` | OpenTelemetry, tracing, health endpoints |
| `Modulus.Testing` | Test harness with per-module SQLite |

## Solution Layout

```
src/
  core/          Modulus.Core, Modulus.AspNetCore
  data/          Abstractions, EF Core, providers (SqlServer, PostgreSQL, MySQL, SQLite, MongoDB)
  identity/      OpenIddict + 6 IdP adapters
  messaging/     Events, Mediator, Inbox, Outbox, RabbitMQ, Kafka, Sagas
  platform/      MultiTenancy, Authorization, BackgroundJobs, Caching, Storage, SignalR
  observability/ Diagnostics, OpenTelemetry
  testing/       WebApplicationFactory harness
  cli/           Modulus.Cli scaffolding tool
tests/
  unit/          15 xUnit test projects
  integration/   Testcontainers-based tests
```
