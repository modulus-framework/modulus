---
sidebar_position: 5
---

# HTTP Idempotency

Modulus provides idempotency for unsafe (mutating) HTTP endpoints.

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    First Request                             │
│                                                              │
│  POST /api/orders                                           │
│  Idempotency-Key: abc-123                                   │
│                                                              │
│  1. Check store: key not found                              │
│  2. Claim key (InProgress)                                  │
│  3. Process request                                         │
│  4. Cache response (2xx only)                               │
│  5. Return response                                         │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    Duplicate Request                         │
│                                                              │
│  POST /api/orders                                           │
│  Idempotency-Key: abc-123                                   │
│                                                              │
│  1. Check store: key found, Completed                       │
│  2. Replay cached response                                  │
│  3. Add header: Idempotency-Replayed: true                  │
└─────────────────────────────────────────────────────────────┘
```

## Setup

```csharp
app.UseModulusIdempotency();
```

## Configuration

```json
{
  "Idempotency": {
    "HeaderName": "Idempotency-Key",
    "Methods": ["POST", "PATCH"],
    "RequireKey": false,
    "ValidateRequestMatch": true,
    "MaxKeyLength": 256,
    "RetentionSeconds": 86400
  }
}
```

## Behavior

| Scenario | Response |
|----------|----------|
| First request | Process normally, cache response |
| Duplicate (completed) | Replay cached response with `Idempotency-Replayed: true` |
| Concurrent duplicate | **409 Conflict** while processing |
| Different payload with same key | **422 Unprocessable Entity** |
| 5xx error | Release key (allows retry) |

## Store

Default: `InMemoryIdempotencyStore` (per-instance, TTL-bounded).

For multi-node deployments, register a distributed store:

```csharp
// Redis
services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

// EF Core
services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
```

Register **before** `AddModulusIdempotency` (uses `TryAdd`).

## Tenant Scoping

Keys are scoped by tenant — they cannot collide across tenants.

## Request Fingerprint

The idempotency check includes a SHA-256 fingerprint of:
- HTTP method
- Path
- Query string
- Request body

A key reused with a different request returns **422**.

## See Also

- [Security Headers](security-headers) — HTTP security
- [Rate Limiting](rate-limiting) — Request throttling
