---
sidebar_position: 9
title: Sagas
description: Long-running process managers on Rebus, wired into Modulus tenancy, logging and the inbox.
---

# Sagas

`Modulus.Sagas` is an opt-in package for long-running processes (sagas, or process managers) built on [Rebus](https://github.com/rebus-org/Rebus), which is MIT-licensed. You write sagas with the standard Rebus `Saga<TData>` API. The package connects them to Modulus.

## Setup

```csharp
builder.Services.AddModulusSagas(b => b
    .Rebus(cfg => cfg
        .Transport(t => /* your transport, e.g. RabbitMQ or Azure Service Bus */)
        .Sagas(s => /* your saga store, e.g. StoreInSql(...) */))
    .PollyRetry(o => o.MaxRetryAttempts = 5)
    .HandlersFromAssemblyOf<OrderProcessSaga>()
    .ReplaceModuleBus()           // IModuleBus.PublishAsync goes through Rebus
    .ReplaceOutboxDispatcher());  // outbox relaying goes through Rebus
```

Transport and saga persistence are provided by you in the `.Rebus(...)` callback. Enable sagas in the same callback (`cfg.Options(o => o.EnableSagas())`) so the saga store is used.

## What the package adds

- **Logging** through Microsoft.Extensions.Logging.
- **Retry and circuit breaker** on incoming messages, using Polly v8.
- **Ambient context.** Publishers stamp `mod-tenant-id` and `mod-correlation-id` headers, and consumers restore them around each handler. Tenant query filters and log correlation then behave as they do on the HTTP path.
- **Handler registration** from the chosen assemblies, and adapters that let existing `IIntegrationEventHandler<T>` implementations run as Rebus handlers. Handlers wrapped by the inbox keep their deduplication.

`ReplaceModuleBus` and `ReplaceOutboxDispatcher` are opt-in. Without them, the in-process bus and dispatcher are used.

## Tenant safety

A message carries a tenant id. Before a handler runs, the tenant is checked against the tenant store. An unknown or inactive tenant sends the message to the dead-letter queue. Enter the verified tenant in the frame that does the work:

```csharp
using var t = sp.EnterTenant(await sp.VerifyTenantAsync(tenantId, ct));
```

An `AsyncLocal` set inside an `async` helper does not flow back to the caller, so enter the tenant in the same frame that does the work.
