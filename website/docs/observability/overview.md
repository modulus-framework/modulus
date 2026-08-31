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

## Distributed Tracing

Traces flow seamlessly across async boundaries and message brokers using W3C trace context:

- **Distributed spans** — Mediator handlers, event processing, outbox dispatch
- **Trace context propagation** — Flows through `TraceParent`/`TraceState` on messages
- **Broker integration** — RabbitMQ headers, Kafka headers carry trace context
- **Activity restoration** — Consumers automatically restore parent context

The `TracingBehavior` adds spans for every command/query:

```
CreateProduct Command
├── Validation
├── Transaction
└── Handler
    └── SaveChanges
```

Publishing an event inside an activity automatically propagates the trace:

```csharp
using var activity = new ActivitySource("MyApp").StartActivity("ProcessOrder");
await bus.PublishAsync(new OrderCreatedEvent { ... });
// Consumer receives activity with same TraceId
```

## Metrics

Performance counters for operational insights:

| Metric | Description |
|--------|-------------|
| **mediator.handler.duration** | Command/query handler execution time (histogram) |
| **outbox.dispatch.lag** | Time from outbox write to dispatch (histogram) |
| **cache.hits** | Cache hit counter |
| **cache.misses** | Cache miss counter |
| **cache.lookup.duration** | Cache lookup time (histogram) |
| **authorization.decision.duration** | Permission check duration (histogram) |
| **module.init.duration** | Module initialization time (histogram) |

Access via `MeterListener` in production telemetry:

```csharp
var listener = new MeterListener();
listener.InstrumentPublished += (instrument, listener) =>
{
    if (instrument.Meter.Name == "Modulus.Mediator")
        listener.EnableMeasurementEvents(instrument);
};
listener.Start();
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
