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
app.MapModulusHealthChecks();
```

## Module Health Checks

Each module can register health checks:

```csharp
public sealed class CatalogModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHealthChecks()
            .AddCheck<CatalogDbHealthCheck>("catalog-db");
    }
}

public sealed class CatalogDbHealthCheck(CatalogDbContext db)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to database");
    }
}
```

## Readiness Behavior

- Returns **200** when all modules are `Healthy` or `Degraded`
- Returns **503** when any module is `Unhealthy`

## Configuration

```json
{
  "HealthChecks": {
    "Enabled": true,
    "Path": "/health"
  }
}
```

## See Also

- [OpenTelemetry](../observability/overview) — Distributed tracing
