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
// Program.cs — place first in the pipeline
builder.Services.AddModulusCorrelation(builder.Configuration);
app.UseModulusCorrelation();

// Resilient outbound client (standard resilience + correlation propagation)
services.AddModulusHttpClient("my-service");
services.AddModulusHttpClient<MyTypedClient>();
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
        var correlationId = correlation.CorrelationId; // string?, plus IsSet
        // Use for logging, tracing, etc.
        return Unit.Value;
    }
}
```

## Outbound Propagation

`AddModulusHttpClient` already wires `CorrelationIdPropagationHandler` as the
outer handler (standard resilience handler + correlation). The handler copies
the current correlation ID to outgoing requests and never overwrites a
caller-set header. W3C `traceparent` is auto-injected by `HttpClient` itself,
so this carries only the business correlation id.

## Background Jobs

Correlation ID flows into background scopes:

```csharp
using var _ = correlation.BeginScope(correlationId);
// All operations within this scope share the correlation ID
```

## See Also

- [OpenTelemetry](../observability/overview) — Distributed tracing
