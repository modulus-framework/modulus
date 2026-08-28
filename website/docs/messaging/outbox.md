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
│  3. COMMIT                                                  │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    Outbox Processor                          │
│                                                              │
│  4. SELECT * FROM OutboxMessages WHERE LockedUntil < NOW()  │
│  5. UPDATE ... SET LockedUntil = NOW() + 30s                │
│  6. Publish to bus                                          │
│  7. DELETE FROM OutboxMessages WHERE Id = @id               │
│     (or mark as failed → retry with backoff)                │
└─────────────────────────────────────────────────────────────┘
```

## Setup

```csharp
// Module composition root
services.AddModuleDatabase<CatalogDbContext>(config);
services.AddOutbox<CatalogDbContext>();
```

## How Events Are Enqueued

When `ModuleDbContext.SaveChangesAsync()` is called:

1. Scans tracked entities for `IIntegrationEvent` instances
2. Builds outbox rows via `OutboxRowFactory`
3. Inserts into `Set<OutboxMessage>()`
4. Calls `base.SaveChangesAsync()` — all in one transaction

```csharp
// In ModuleDbContext
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    if (_outboxWriter is not null)
    {
        foreach (var entry in ChangeTracker.Entries<IIntegrationEvent>())
        {
            _outboxWriter.Enqueue(entry.Entity);
        }
    }
    return await base.SaveChangesAsync(ct);
}
```

## Outbox Processor

The processor runs as a background service:

```csharp
// Automatically registered by AddOutbox<TContext>()
services.AddHostedService<OutboxProcessor>();
```

### Features

| Feature | Description |
|---------|-------------|
| **Atomic claim** | `ExecuteUpdateAsync` with `WHERE LockedUntil < NOW()` |
| **Exponential backoff** | Failed events retry with increasing delay |
| **Dead letter** | Events exceeding `MaxRetries` are logged and marked |
| **Row locking** | Prevents duplicate processing across instances |

## Configuration

```json
{
  "Outbox": {
    "PollingIntervalMs": 1000,
    "MaxRetries": 5,
    "BatchSize": 10
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
    public Guid? TenantId { get; set; }
    public DateTime LockedUntil { get; set; }
    public int RetryCount { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

## Multi-Context Support

Each module has its own outbox:

```csharp
// Catalog module
services.AddOutbox<CatalogDbContext>();

// Orders module
services.AddOutbox<OrdersDbContext>();
```

Each context runs its own `OutboxProcessor`.

## See Also

- [Inbox](inbox) — Consumer-side deduplication
- [Events](events) — Integration event definitions
