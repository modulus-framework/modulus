---
sidebar_position: 1
---

# Observability Overview

Modulus provides built-in observability via OpenTelemetry.

## Components

| Component | Purpose |
|-----------|---------|
| **Tracing** | Distributed trace collection |
| **Metrics** | Performance counters |
| **Health Endpoints** | Module health aggregation |
| **Graph Endpoint** | Module dependency visualization |

## Setup

```csharp
services.AddModulusObservability(config);
```

## Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/health/modules` | Per-module health aggregation |
| `/health/graph` | Module dependency graph |

## Tracing

The `TracingBehavior` adds spans for every command/query:

```
CreateProduct Command
├── Validation
├── Transaction
└── Handler
    └── SaveChanges
```

## Configuration

```json
{
  "Observability": {
    "ServiceName": "MyApp",
    "EnableTracing": true,
    "EnableMetrics": true,
    "Exporters": ["otlp"]
  }
}
```

## See Also

- [OpenTelemetry](opentelemetry) — Detailed configuration
