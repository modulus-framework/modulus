---
sidebar_position: 2
---

# OpenTelemetry

Modulus integrates with OpenTelemetry for distributed tracing and metrics.

## Setup

```csharp
services.AddModulusObservability(config);
```

## Configuration

```json
{
  "OpenTelemetry": {
    "ServiceName": "MyApp",
    "ServiceVersion": "1.0.0",
    "EnableTracing": true,
    "EnableMetrics": true,
    "Exporters": {
      "Otlp": {
        "Endpoint": "http://localhost:4317"
      }
    }
  }
}
```

## Tracing

### TracingBehavior

Automatically creates spans for mediator operations:

```csharp
// Registered automatically by AddModulusObservability
// Wraps every command/query with an Activity span
```

### Custom Spans

```csharp
using var activity = ModulusActivitySources.Start("MyOperation");

// Your logic here
activity?.SetTag("product.id", productId);
```

## Metrics

Built-in metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `modulus.commands.duration` | Histogram | Command execution time |
| `modulus.queries.duration` | Histogram | Query execution time |
| `modulus.outbox.processed` | Counter | Outbox events processed |
| `modulus.inbox.processed` | Counter | Inbox events processed |

## Exporters

| Exporter | Protocol | Use Case |
|----------|----------|----------|
| **OTLP** | gRPC/HTTP | Jaeger, Tempo, SigNoz |
| **Zipkin** | HTTP | Zipkin |
| **Console** | stdout | Development |

## Health Checks

```csharp
// Program.cs
app.MapModulusDiagnostics(app);
```

Provides:
- `/health/modules` — Per-module health status
- `/health/graph` — Module dependency graph (mermaid format)

## See Also

- [Health Checks](../hardening/health-checks) — Liveness/readiness probes
- [Correlation](../hardening/correlation) — Request correlation
