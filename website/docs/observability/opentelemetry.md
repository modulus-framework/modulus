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

## Local stack and backend options

The repository ships a ready-to-run stack in `deploy/observability`: an OpenTelemetry Collector in front of Jaeger (traces),
VictoriaLogs (logs) and Prometheus (metrics), all Apache-2.0. Start it with
`docker compose -f deploy/observability/docker-compose.yml up -d` and set `OpenTelemetry:Otlp:Endpoint` to `http://localhost:4317`.

Alternative backends live in `deploy/observability/backends`: Grafana with Loki and Tempo, OpenObserve, OpenSearch, Apache
SkyWalking and Netdata. Run one at a time; each has its own compose file. The Grafana stack, OpenObserve and Netdata are AGPL-3.0 or
GPL-3.0, so check your own license requirements before using them. The README in that folder lists what was tested for each option.

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
