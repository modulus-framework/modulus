---
sidebar_position: 2
---

# Health Checks

Modulus provides liveness and readiness health probes.

## Endpoints

| Endpoint | Purpose | Response |
|----------|---------|----------|
| `/health/live` | Liveness (no dependency I/O) | 200 OK |
| `/health/ready` | Readiness (aggregates module health) | 200 OK or 503 |

## Setup

```csharp
builder.Services.AddHealthChecks()
    .AddModulusHealthChecks();   // bridges IModuleHealthCheck into the ecosystem

app.MapModulusHealthChecks();    // /health/live + /health/ready (paths overridable)
```

## Module Health Checks

Each module implements the framework seam (not `IHealthCheck` directly):

```csharp
public sealed class CatalogDbHealthCheck(CatalogDbContext db)
    : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct)
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        return canConnect
            ? new ModuleHealthResult("catalog-db", HealthStatus.Healthy, "OK", TimeSpan.Zero)
            : new ModuleHealthResult("catalog-db", HealthStatus.Unhealthy, "Cannot connect", TimeSpan.Zero);
    }
}
```

Checks run isolated: a throwing check reports `Unhealthy` (never a probe
500), and a hung check times out after 5s. This is separate from
Observability's `/health/modules` aggregator.

## Readiness Behavior

- Returns **200** when all modules are `Healthy` or `Degraded`
- Returns **503** when any module is `Unhealthy`

Paths default to `/health/live` + `/health/ready` (method arguments, no
config section).

## See Also

- [OpenTelemetry](../observability/overview) — Distributed tracing
