---
sidebar_position: 6
---

# Request Correlation

Modulus provides automatic correlation ID propagation across services.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Inbound Request                           │
│                                                              │
│  GET /api/orders                                            │
│  X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000    │
│                                                              │
│  1. Middleware adopts header (or generates from trace id)    │
│  2. Pushes to ICorrelationContext (AsyncLocal)               │
│  3. Tags Activity.Current with correlation.id                │
│  4. Echoes on response                                      │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    Outbound Request                          │
│                                                              │
│  CorrelationIdPropagationHandler copies header               │
│  to outgoing requests (never overwrites caller-set header)  │
└─────────────────────────────────────────────────────────────┘
```

## Setup

```csharp
// Program.cs
app.UseModulusCorrelation();

// For outbound HTTP clients
services.AddModulusHttpClient("my-service");
```

## Configuration

```json
{
  "Correlation": {
    "HeaderName": "X-Correlation-ID",
    "IncludeInResponse": true,
    "UseTraceIdWhenMissing": true
  }
}
```

## ICorrelationContext

```csharp
public sealed class MyHandler(ICorrelationContext correlation)
    : ICommandHandler<MyCommand, Unit>
{
    public async Task<Unit> HandleAsync(MyCommand command, CancellationToken ct)
    {
        var correlationId = correlation.Id;
        // Use for logging, tracing, etc.
        return Unit.Value;
    }
}
```

## Outbound Propagation

```csharp
services.AddModulusHttpClient("inventory-service")
    .AddHttpMessageHandler<CorrelationIdPropagationHandler>();
```

The handler automatically copies the current correlation ID to outgoing requests.

## Background Jobs

Correlation ID flows into background scopes:

```csharp
using var _ = correlation.BeginScope(correlationId);
// All operations within this scope share the correlation ID
```

## See Also

- [OpenTelemetry](../observability/overview) — Distributed tracing
