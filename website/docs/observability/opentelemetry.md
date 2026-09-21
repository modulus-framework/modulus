---
sidebar_position: 2
---

# OpenTelemetry

Modulus integrates with OpenTelemetry for distributed tracing and metrics.

## Setup

Config-bound bootstrap (binds the `OpenTelemetry` section, wires ASP.NET Core
+ HttpClient + Runtime instrumentation plus the Modulus sources/meters):

```csharp
services.AddModulusOpenTelemetry(builder.Configuration, builder.Environment);
```

For manual control, compose the provider yourself and add the Modulus
sources/meters to your own builder:

```csharp
services.AddOpenTelemetry()
    .WithTracing(b => b.UseModulusTracing().AddAspNetCoreInstrumentation())
    .WithMetrics(b => b.UseModulusMetrics().AddRuntimeInstrumentation());
services.AddModulusObservability(); // registers TracingBehavior only
```

## Configuration

```json
{
  "OpenTelemetry": {
    "Enabled": true,
    "ServiceName": "MyApp",
    "EnableConsoleExporter": false,
    "Otlp": {
      "Endpoint": "http://localhost:4317",
      "ExportTraces": true,
      "ExportMetrics": true
    }
  }
}
```

Exporters are only added when explicitly enabled — OTLP needs an endpoint, so
an empty/missing endpoint disables OTLP export and the app boots with no
export overhead. `Enabled: false` skips telemetry entirely.

## Tracing

### TracingBehavior

Automatically creates spans for mediator operations (registered by
`AddModulusObservability`, which `AddModulusOpenTelemetry` calls for you):

```csharp
// Wraps every command/query with an Activity span sharing the host provider
```

### Custom Spans

```csharp
using var activity = ModulusActivitySources.Start("MyOperation");

// Your logic here
activity?.SetTag("product.id", productId);
```

## Metrics

Built-in instruments:

| Metric | Type | Description |
|--------|------|-------------|
| `modulus.mediator.handler.duration` | Histogram | Handler execution time (ms) |
| `modulus.module.init.duration` | Histogram | Module initialization time (ms) |
| `modulus.outbox.dispatch_lag` | Histogram | Creation-to-dispatch lag (ms) |
| `modulus.events.publish.duration` | Histogram | Broker publish time to ack (ms) |
| `modulus.cache.lookup.duration` | Histogram | Cache lookup time (ms) |
| `modulus.cache.hits` / `modulus.cache.misses` | Counter | Cache hits/misses |
| `modulus.authorization.decision.duration` | Histogram | Auth decision time (ms) |
| `modulus.authorization.allowed` / `.denied` | Counter | Auth decisions |

Plus counters for outbox deferrals (`OutboxDeferred`), inbox dedup hits,
rate limiting, and tenant-resolution failures (see `ModulusMeters`).

## Exporters

| Exporter | Protocol | Use Case |
|----------|----------|----------|
| **OTLP** | gRPC/HTTP | Jaeger, Tempo, SigNoz, any OTLP backend |
| **Console** | stdout | Development (`EnableConsoleExporter`) |

## Health Checks

```csharp
// Program.cs
app.MapModulusDiagnostics(app);
```

Provides:
- `/health/modules` — Per-module health status
- `/health/graph` — Loaded-module inventory in registration order (`name`, `type`, `initOrder`)

## See Also

- [Health Checks](../hardening/health-checks) — Liveness/readiness probes
- [Correlation](../hardening/correlation) — Request correlation
