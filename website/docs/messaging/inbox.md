---
sidebar_position: 5
---

# Inbox

The inbox pattern provides idempotent message consumption — each integration
event is processed once per handler, even when the broker redelivers.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Event arrives                             │
│                                                              │
│  1. Claim row (EventId, HandlerName):                        │
│     INSERT or atomic claim — loser of a concurrent race      │
│     defers via InboxDeferralException (redeliver later)      │
│  2. If already Processed → skip (already done)               │
│  3. Run the inner handler                                    │
│  4. Mark Processed (or Failed with backoff; dead-letter      │
│     after MaxRetries)                                        │
└─────────────────────────────────────────────────────────────┘
```

The claim key is the **composite (`Id` = EventId, `HandlerName`)** — the EF
primary key is `(Id, HandlerName)` and Mongo uses a unique compound index, so
an event with several handlers runs *each* handler once. Statuses are the
`InboxStatus` enum (`Pending`/`Processing`/`Processed`/`Failed`); claims hold
a lease (`ClaimTimeoutSeconds`) after which another worker may reclaim them.

## Setup

### EF Core

```csharp
services.AddInbox<CatalogDbContext>();
```

Binds an `EfInboxStore` to the named context and contributes the
`InboxMessage` mapping into every `ModuleDbContext` — no hand-wiring needed.

### MongoDB

```csharp
services.AddMongoInbox();
```

Registers the collection, `MongoInboxStore`, and an index initializer
(unique compound index on `(EventId, HandlerName)`).

## How It Works

The inbox wraps `IIntegrationEventHandler<T>` **at dispatch time**: the
dispatchers (`IntegrationEventDispatcher`, in-process bus) wrap each resolved
handler in the idempotent decorator when they dispatch — after every handler
*and* every inbox registration has run, regardless of `Program.cs` ordering.

```csharp
// Your handler
public sealed class ProductCreatedHandler
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent @event)
    {
        // This runs once per event per handler
        await _inventory.InitializeStockAsync(@event.Id);
    }
}

// The decorator (auto-registered by AddInbox/AddMongoInbox)
// 1. Claims (EventId, HandlerName) — concurrent loser defers
// 2. If not already processed, calls your handler
// 3. Marks processed / failed with backoff
```

Rows written before the `HandlerName` column existed are honoured for any
handler claiming that EventId (legacy `Processed`/dead-lettered rows are
skipped; eligible legacy rows are adopted by the first claimant).

## Configuration

```json
{
  "Inbox": {
    "MaxRetries": 5,
    "ClaimTimeoutSeconds": 300,
    "HandlerRetryCount": 3,
    "HandlerRetryBaseDelaySec": 2,
    "HandlerRetryExponential": true,
    "HandlerRetryJitter": true
  }
}
```

In-handler transient failures retry in-pipeline before the claim is released;
only exhausted claims hit the store-level backoff.

## InboxMessage Entity

```csharp
public class InboxMessage
{
    public Guid Id { get; set; }            // = EventId
    public string HandlerName { get; set; } = default!;  // "" = legacy row
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public InboxStatus Status { get; set; } // Pending/Processing/Processed/Failed
    public Guid TenantId { get; set; }
    public string ModuleName { get; set; } = default!;
    public int RetryCount { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? CorrelationId { get; set; }
}
```

## Deduplication

| Scenario | Behavior |
|----------|----------|
| First delivery | Claims (EventId, HandlerName), processes, marks processed |
| Redelivery (processed) | Skips (already processed) |
| Concurrent redelivery | Loser throws `InboxDeferralException` (redeliver later) |
| Failed delivery | In-pipeline retries, then backoff up to `MaxRetries` |
| Exceeded retries | Dead-letters with error log |

## See Also

- [Outbox](outbox) — Producer-side reliable publishing
- [Events](events) — Integration event definitions
