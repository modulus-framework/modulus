---
sidebar_position: 7
---

# Feature Flags

Modulus integrates `Microsoft.FeatureManagement` for feature toggling.

## Setup

```csharp
services.AddModulusFeatureFlags(config);
```

## Configuration

Binds the `FeatureManagement` section (the library's own convention).
Only the `Percentage` and `TimeWindow` filters are registered:

```json
{
  "FeatureManagement": {
    "NewCheckout": true,
    "BetaReporting": false,
    "AdvancedSearch": {
      "EnabledFor": [
        { "Name": "Percentage", "Parameters": { "Value": 25 } },
        { "Name": "TimeWindow", "Parameters": { "Start": "2026-01-01T00:00:00Z", "End": "2026-06-01T00:00:00Z" } }
      ]
    }
  }
}
```

## Usage in Controllers

```csharp
[ApiController]
[FeatureGate("NewCheckout")]
[Route("api/checkout")]
public sealed class NewCheckoutController : ControllerBase
{
    // Only available when NewCheckout feature is enabled
}
```

## Usage in Minimal APIs

```csharp
app.MapPost("/api/checkout", HandleCheckout)
    .RequireFeature("NewCheckout");
```

When the flag is off, the endpoint returns **404** (hiding it from clients) —
the equivalent of MVC's `[FeatureGate]`. Evaluation is scoped (filters can
read the ambient tenant/user) and gated endpoints additionally require the
`IFeatureGate` entitlement: both the entitlement **and** the rollout must
allow.

## Usage in Handlers

```csharp
public sealed class GetReportHandler(IFeatureManager features)
    : IQueryHandler<GetReport, ReportDto>
{
    public async Task<ReportDto> HandleAsync(GetReport query, CancellationToken ct)
    {
        if (await features.IsEnabledAsync("BetaReporting"))
        {
            return await GetNewReportAsync(query, ct);
        }
        return await GetLegacyReportAsync(query, ct);
    }
}
```

## Filters

Built-in filters:

| Filter | Description |
|--------|-------------|
| `Percentage` | Enable for a percentage of requests |
| `TimeWindow` | Enable during a time range |

## See Also

- [Secrets Guard](secrets-guard) — Startup validation
