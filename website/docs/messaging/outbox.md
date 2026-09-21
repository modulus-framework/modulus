---
sidebar_position: 4
---

# Outbox

The transactional outbox pattern ensures reliable event publishing by storing events in the database within the same transaction as business data.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Write Transaction                         │
│                                                              │
│  1. INSERT INTO Products (...)                               │
│  2. INSERT INTO OutboxMessages (...)  ← Same transaction    │
│  3. COMMIT                                                   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    Outbox Processor (polling)                │
│                                                              │
│  4. Claim due rows atomically (ExecuteUpdateAsync,          │
│     WHERE re-checks LockedUntil/NextAttemptAt)              │
│  5. Publish to bus (in-process IModuleBus by default)       │
│  6. Mark ProcessedAt on success; on failure schedule       │
│     NextAttemptAt with exponential backoff, dead-letter     │
│     after MaxRetries                                        │
└─────────────────────────────────────────────────────────────┘
```

Dispatch-then-mark is inherently non-atomic, so delivery is **at-least-once**:
consumers must dedupe via the [inbox](inbox).

## Setup

```csharp
// Module composition root (per module DbContext)
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlServer(config.GetConnectionString("Catalog")));
services.AddOutbox<CatalogDbContext>();
```

## How Events Are Enqueued

`ModuleDbContext.SaveChangesAsync()` collects domain events from tracked
`IAggregateRoot.DomainEvents`, keeps those implementing `IIntegrationEvent`,
and — only when an `IOutboxWriter` is registered (i.e. the app called
`AddOutbox<TContext>`) — inserts rows into its own `Set<OutboxMessage>()`
*before* `base.SaveChangesAsync()`, so outbox rows participate in the same DB
transaction. Rows are built by the shared `OutboxRowFactory` (tenant —
`Guid.Empty` means host — correlation/causation ids, trace context, module name).

## Outbox Processor

`OutboxProcessor` is registered **scoped** (one instance per polling cycle);
the background loop is the `OutboxPollingService` hosted service (registered
once; `DisableAutoPolling` disables it at runtime, e.g. for tests).

### Features

| Feature | Description |
|---------|-------------|
| **Atomic claim** | `ExecuteUpdateAsync` whose `WHERE` re-checks `LockedUntil` (provider-agnostic `FOR UPDATE SKIP LOCKED`); sets `LockedBy` (instance id) + fresh `LockedUntil` |
| **Redelivery deferral** | `InboxDeferralException` reschedules `NextAttemptAt` by `DeferDelaySec` without burning the retry budget |
| **Exponential backoff** | Failed dispatches schedule `NextAttemptAt`; dead-letter (with error log) after `MaxRetries` |
| **Row locking** | Expired locks are reclaimed, so crashed instances don't stall dispatch |
| **Purge** | Dispatched/dead rows older than `PurgeAfterDays` are deleted in bounded batches |
| **Leader election** | `EnableLeaderElection` acquires an `IDistributedLock` per cycle so only one replica polls |

## Configuration

```json
{
  "Outbox": {
    "PollingIntervalSec": 5,
    "BatchSize": 100,
    "MaxRetries": 5,
    "LockTimeoutSec": 30,
    "InitialBackoffSec": 2,
    "DeferDelaySec": 15,
    "Dispatcher": "in-process",
    "DisableAutoPolling": false,
    "EnableLeaderElection": false,
    "PurgeAfterDays": 7
  }
}
```

## OutboxMessage Entity

```csharp
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public Guid TenantId { get; set; }          // Guid.Empty = host
    public string ModuleName { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public int? SchemaVersion { get; set; }
}
```

## Multi-Context Support

One processor drains **all** registered module `DbContext`s per cycle
(deduped by runtime type); one polling service total:

```csharp
// Catalog module
services.AddOutbox<CatalogDbContext>();

// Orders module
services.AddOutbox<OrdersDbContext>();
```

For cross-module consistency prefer the transactional outbox over
multi-context transactions (each context commits independently).

## Management API

Operators can list, inspect, replay, and purge dead-lettered messages
(`Modulus.Outbox.Management`, all endpoints require the `messaging:manage`
permission):

```csharp
services.AddModulusOutboxManagement();   // declares the permission
app.MapModulusOutboxManagement();        // GET/POST/DELETE /outbox/…
```

| Endpoint | Behavior |
|----------|----------|
| `GET /outbox/dead-letters?page&pageSize&moduleFilter&tenantFilter` | Dead-lettered rows, filters pushed to the DB, bounded page fetch per context |
| `GET /outbox/dead-letters/{id}` | Single dead-lettered message with payload |
| `POST /outbox/replay` | Resets dead-lettered rows for retry (batched); the cleared error is archived to the log with the acting user |
| `DELETE /outbox/dead-letters/purge?beforeDays=30` | Deletes old dead-lettered rows |

MongoDB apps map `MapModulusMongoOutboxManagement()` instead (same models and
permission, server-side filter/sort/skip/limit).

## MongoDB Writer

`MongoOutboxWriter` inserts into the outbox collection. Implement
`IMongoOutboxSessionProvider` and flow the ambient `IClientSessionHandle`
from the unit of work to make domain writes + outbox insert commit atomically
via a multi-document transaction (requires a replica set); without a session
it falls back to a plain insert (dual-write gap — consumers still dedup via
inbox).

## See Also

- [Inbox](inbox) — Consumer-side deduplication
- [Events](events) — Integration event definitions
