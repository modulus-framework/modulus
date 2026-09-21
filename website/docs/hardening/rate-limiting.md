---
sidebar_position: 1
---

# Rate Limiting

Modulus provides built-in rate limiting partitioned by user, tenant, IP, or globally.

## Setup

Both calls are required — `Use…` alone does nothing without registration:

```csharp
builder.Services.AddModulusRateLimiting(builder.Configuration);
app.UseModulusRateLimiting();
```

## Configuration

```json
{
  "RateLimiting": {
    "Enabled": true,
    "PermitLimit": 100,
    "WindowSeconds": 60,
    "QueueLimit": 0,
    "Partition": "User",
    "RejectionStatusCode": 429
  }
}
```

## Partitioning

| Strategy | Description |
|----------|-------------|
| `User` | Per authenticated user (falls back to IP for anonymous) |
| `Tenant` | Per tenant (falls back to IP when no tenant is in scope) |
| `IpAddress` | Per client IP address |
| `Global` | Single limit for all requests |

## Usage

The framework wires a single fixed-window `GlobalLimiter` applied globally
via middleware. For custom per-endpoint limiters, use ASP.NET Core's
`AddRateLimiter` directly — the framework does not provide named limiters:

```csharp
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("orders", opts =>
    {
        opts.PermitLimit = 10;
        opts.Window = TimeSpan.FromSeconds(60);
    });
});

app.MapPost("/api/orders", HandleOrder)
    .RequireRateLimiting("orders");
```

## Response

When rate limited, returns `429 Too Many Requests` with `Retry-After` header.

## See Also

- [Health Checks](health-checks) — Monitoring
- [Security Headers](security-headers) — Protection
