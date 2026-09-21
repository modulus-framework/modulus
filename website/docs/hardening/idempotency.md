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

Place after `UseModulus()` so the tenant is resolved before keys are scoped,
still wrapping the controllers so responses can be replayed:

```csharp
builder.Services.AddModulusIdempotency(builder.Configuration);
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
    "MaxKeyLength": 255,
    "RetentionSeconds": 86400,
    "MaxResponseBytes": 1048576
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
| Missing/overlong key with `RequireKey` | **400 Bad Request** |
| 5xx error | Release key (allows retry) |
| Response over `MaxResponseBytes` | Not cached — still runs; retry re-runs |

## Store

Default: `InMemoryIdempotencyStore` (per-instance, TTL-bounded).

For multi-node deployments, register your own `IIdempotencyStore` (e.g.
Redis- or EF-backed) **before** `AddModulusIdempotency` (`TryAdd` leaves a
prior registration in place).

## Tenant Scoping

Keys are scoped by tenant **and** authenticated user — they cannot collide or
leak responses across tenants.

## Request Fingerprint

The idempotency check includes a SHA-256 fingerprint of:
- HTTP method
- Path
- Query string
- Request body
- Content-Type

A key reused with a different request returns **422**.

## See Also

- [Security Headers](security-headers) — HTTP security
- [Rate Limiting](rate-limiting) — Request throttling
