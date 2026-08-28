---
sidebar_position: 1
---

# Rate Limiting

Modulus provides built-in rate limiting partitioned by user, tenant, IP, or globally.

## Setup

```csharp
app.UseModulusRateLimiting();
```

## Configuration

```json
{
  "RateLimiting": {
    "Enabled": true,
    "PermitLimit": 100,
    "WindowSeconds": 60,
    "PartitionBy": "User"
  }
}
```

## Partitioning

| Strategy | Description |
|----------|-------------|
| `User` | Per authenticated user (falls back to IP) |
| `Tenant` | Per tenant |
| `IP` | Per client IP address |
| `Global` | Single limit for all requests |

## Usage

The rate limiter is applied globally via middleware. Specific endpoints can have custom limits:

```csharp
app.MapPost("/api/orders", HandleOrder)
    .RequireRateLimiting("orders");
```

## Custom Limiters

```csharp
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("orders", opts =>
    {
        opts.PermitLimit = 10;
        opts.Window = TimeSpan.FromSeconds(60);
    });
});
```

## Response

When rate limited, returns `429 Too Many Requests` with `Retry-After` header.

## See Also

- [Health Checks](health-checks) — Monitoring
- [Security Headers](security-headers) — Protection
