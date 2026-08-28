---
sidebar_position: 4
---

# Security Headers

Modulus adds security headers to all responses.

## Setup

```csharp
app.UseModulusSecurityHeaders();
```

## Headers Added

| Header | Value | Condition |
|--------|-------|-----------|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | HTTPS only |
| `X-Content-Type-Options` | `nosniff` | Always |
| `X-Frame-Options` | `DENY` | Always |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Always |
| `Content-Security-Policy` | Configurable | Optional |
| `Permissions-Policy` | Configurable | Optional |
| `Server` | (removed) | Always |

## Configuration

```json
{
  "SecurityHeaders": {
    "Enabled": true,
    "HstsMaxAgeDays": 365,
    "IncludeSubDomains": true,
    "ContentSecurityPolicy": "default-src 'self'",
    "PermissionsPolicy": "camera=(), microphone=()"
  }
}
```

## CSP

Content Security Policy restricts resource loading:

```json
{
  "SecurityHeaders": {
    "ContentSecurityPolicy": "default-src 'self'; script-src 'self' 'unsafe-inline'"
  }
}
```

## Permissions Policy

Controls browser features:

```json
{
  "SecurityHeaders": {
    "PermissionsPolicy": "camera=(), microphone=(), geolocation=()"
  }
}
```

## See Also

- [CORS](cors) — Cross-origin configuration
- [Rate Limiting](rate-limiting) — Request throttling
