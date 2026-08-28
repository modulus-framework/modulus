---
sidebar_position: 5
---

# Inbox

The inbox pattern provides idempotent message consumption — ensuring each integration event is processed exactly once.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Event arrives                             │
│                                                              │
│  1. Check InboxMessages WHERE EventId = @id                 │
│  2. If EXISTS and Status = Completed → skip (already done)  │
│  3. If EXISTS and Status = Processing → 409 (concurrent)    │
│  4. INSERT INTO InboxMessages (EventId, Status = Processing)│
│  5. Process event                                           │
│  6. UPDATE Status = Completed                               │
│     (or on failure: increment RetryCount, schedule backoff) │
└─────────────────────────────────────────────────────────────┘
```

## Setup

### EF Core

```csharp
services.AddInbox<CatalogDbContext>();
```

### MongoDB

```csharp
services.AddMongoInbox();
```

## How It Works

The inbox decorates `IIntegrationEventHandler<T>` with an idempotent wrapper:

```csharp
// Your handler
public sealed class ProductCreatedHandler
    : IIntegrationEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent @event)
    {
        // This only runs once per event
        await _inventory.InitializeStockAsync(@event.ProductId);
    }
}

// The decorator (auto-registered by AddInbox)
// 1. Checks if event was already processed
// 2. If not, claims the row and calls your handler
// 3. Marks as completed or failed
```

## Configuration

```json
{
  "Inbox": {
    "MaxRetries": 5,
    "DefaultTtlMinutes": 1440
  }
}
```

## InboxMessage Entity

```csharp
public class InboxMessage
{
    public Guid EventId { get; set; }  // PK
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public string Status { get; set; } = default!;  // Processing/Completed/Failed
    public int RetryCount { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

## Deduplication

| Scenario | Behavior |
|----------|----------|
| First delivery | Claims row, processes, marks completed |
| Redelivery (completed) | Skips (already processed) |
| Redelivery (processing) | Waits or defers (concurrent handling) |
| Failed delivery | Retries with backoff up to `MaxRetries` |
| Exceeded retries | Dead-letters with error log |

## See Also

- [Outbox](outbox) — Producer-side reliable publishing
- [Events](events) — Integration event definitions
