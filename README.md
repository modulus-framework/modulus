# Modulus Framework

An enterprise-grade **modular monolith** framework for .NET 10, built with DDD, CQRS,
event-driven architecture, and first-class multi-tenancy support.

## Overview

Modulus is designed for teams who need the architectural rigour of ABP or eShop
without the heavyweight abstractions. It provides proven building blocks that compose
cleanly — pick only what your application needs.

## Architecture

```
src/
├── core/
│   ├── Modulus.Core.Abstractions      # Domain primitives: AggregateRoot, IDomainEvent, ValueObject
│   ├── Modulus.Core                    # Base implementations and shared utilities
│   └── Modulus.AspNetCore              # ASP.NET Core wiring, middleware, module host
│
├── messaging/
│   ├── Modulus.Mediator.Abstractions   # IRequest, IRequestHandler, pipeline behaviors
│   ├── Modulus.Mediator                # In-process CQRS mediator with validation/logging behaviors
│   ├── Modulus.Events.Abstractions     # IIntegrationEvent, IModuleBus, IIntegrationEventHandler
│   ├── Modulus.Events                  # DomainEventDispatcher, InProcessModuleBus, registry
│   ├── Modulus.EventBus.RabbitMQ       # RabbitMQ event bus (topic exchange, auto-reconnect consumer)
│   ├── Modulus.EventBus.Kafka          # Kafka event bus (idempotent producer, consumer groups)
│   ├── Modulus.Outbox.Abstractions     # OutboxMessage, IOutboxWriter, IOutboxDispatcher
│   ├── Modulus.Outbox                  # EF Core outbox: writer, processor, polling service
│   ├── Modulus.Outbox.MongoDB          # MongoDB outbox store
│   ├── Modulus.Inbox.Abstractions      # Inbox for idempotent message processing
│   ├── Modulus.Inbox                   # Default inbox processor
│   └── Modulus.Inbox.MongoDB           # MongoDB inbox store
│
├── data/
│   ├── Modulus.Data.Abstractions       # IRepository, IUnitOfWork, auditing interfaces
│   ├── Modulus.EntityFrameworkCore.Abstractions  # DbContext base, specifications
│   ├── Modulus.EntityFrameworkCore     # ModuleDbContext, domain event collection, SaveChanges
│   ├── Modulus.Data.SqlServer          # SQL Server provider
│   ├── Modulus.Data.PostgreSQL         # PostgreSQL (Npgsql) provider
│   ├── Modulus.Data.MySQL              # MySQL (Oracle) provider
│   ├── Modulus.Data.SQLite             # SQLite provider
│   ├── Modulus.Data.MongoDB            # MongoDB driver wrapper
│   ├── Modulus.Data.Redis              # StackExchange.Redis wrapper
│   ├── Modulus.Data.Elasticsearch      # Elastic 9.x client
│   ├── Modulus.Data.Cassandra          # CassandraCSharpDriver
│   ├── Modulus.Data.CosmosDB           # Azure Cosmos SDK
│   └── Modulus.Data.DynamoDB           # AWS DynamoDB
│
├── identity/
│   ├── Modulus.Identity.Abstractions   # ModulusUser, ModulusRole, IExternalIdentityProvider
│   ├── Modulus.Identity                # OpenIddict server, token endpoints, current-user
│   ├── Modulus.Identity.EntityFrameworkCore  # Identity + OpenIddict EF mappings
│   ├── Modulus.Identity.Auth0          # Auth0 OIDC adapter
│   ├── Modulus.Identity.Authentik      # Authentik OIDC adapter
│   ├── Modulus.Identity.AzureAd        # Azure AD OIDC adapter
│   ├── Modulus.Identity.Duende         # Duende IdentityServer adapter
│   ├── Modulus.Identity.Keycloak       # Keycloak OIDC adapter
│   └── Modulus.Identity.Okta           # Okta OIDC adapter
│
├── platform/
│   ├── Modulus.MultiTenancy            # Tenant resolution, per-tenant DB, connection resolver
│   ├── Modulus.Authorization           # Permission system, policy-based authorization
│   ├── Modulus.BackgroundJobs          # IJobScheduler abstraction
│   ├── Modulus.BackgroundJobs.Hangfire # Hangfire scheduler + DI integration
│   ├── Modulus.SignalR.Abstractions    # IBackplane abstraction
│   ├── Modulus.SignalR                 # Hub base classes, group management
│   ├── Modulus.SignalR.Redis           # Redis backplane
│   └── Modulus.SignalR.Azure           # Azure SignalR Service backplane
│
└── observability/
    ├── Modulus.Diagnostics             # Correlation IDs, diagnostic context
    ├── Modulus.OpenTelemetry           # OTel traces/metrics/logs auto-wiring
    └── Modulus.Benchmarks              # BenchmarkDotNet harness
```

## Key Features

### Domain-Driven Design
- `AggregateRoot<TId>` with domain event collection
- `ValueObject` base with structural equality
- `IDomainEvent` dispatched automatically after `SaveChangesAsync`

### CQRS Mediator
- `IRequest<TResponse>` / `IRequestHandler<T, R>` — no MediatR dependency
- Open-generic pipeline behaviors: validation, logging, caching
- Assembly scanning for handler registration

### Event Bus — 3 Providers
```csharp
services.AddModulusEvents(typeof(MyHandler).Assembly);

// Pick ONE:
services.AddInMemoryEventBus();                                       // in-process
services.AddRabbitMqEventBus(o => o.HostName = "rabbitmq");           // RabbitMQ
services.AddKafkaEventBus(o => o.BootstrapServers = "kafka:9092");    // Kafka
```
All three implement `IModuleBus` and integrate seamlessly with the Outbox pattern.

### Transactional Outbox
- EF Core outbox with background `OutboxPollingService`
- Automatic `IOutboxDispatcher` → `IModuleBus` pipeline
- MongoDB outbox store available for non-relational deployments

### Inbox Pattern
- Idempotent message deduplication
- MongoDB inbox store

### Multi-Tenancy
- `ICurrentTenant` with resolution from header, claim, or subdomain
- Per-tenant connection-string resolver
- Tenant-scoped filtering on `ModuleDbContext`

### Identity & Authentication
- OpenIddict-based token server (password, client-credentials, refresh-token grants)
- 6 external IdP adapters: Auth0, Authentik, Azure AD, Duende, Keycloak, Okta
- `ICurrentUser` with claims-based implementation

### Data Providers
- **Relational:** SQL Server, PostgreSQL, MySQL, SQLite (EF Core 10)
- **NoSQL:** MongoDB, Redis, Elasticsearch, Cassandra, Cosmos DB, DynamoDB

### Observability
- OpenTelemetry auto-instrumentation (ASP.NET Core, EF Core, HTTP client)
- Correlation ID propagation
- BenchmarkDotNet harness for performance testing

## Build

```bash
dotnet build modulus.slnx
```

The solution compiles with **0 errors, 0 warnings** (`TreatWarningsAsErrors` is enabled).
Central Package Management is used via `Directory.Packages.props`.

## Target Framework

- **.NET 10** (`net10.0`)
- SDK 10.0.109 or later

## Dependencies (Major)

| Library | Version |
|---------|---------|
| EF Core | 10.0.9 |
| OpenIddict | 7.5.0 |
| MongoDB.Driver | 3.9.0 |
| StackExchange.Redis | 3.0.0 |
| RabbitMQ.Client | 7.2.1 |
| Confluent.Kafka | 2.14.2 |
| Hangfire | 1.8.23 |
| OpenTelemetry | 1.16.0 |
| Elastic.Clients.Elasticsearch | 9.4.2 |

## License

Proprietary — All rights reserved.
