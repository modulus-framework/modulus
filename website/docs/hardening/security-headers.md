---
sidebar_position: 4
---

# Security Headers

Modulus adds security headers to all responses.

## Setup

```csharp
builder.Services.AddModulusSecurityHeaders(builder.Configuration);
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
    "ContentTypeOptions": true,
    "FrameOptions": "DENY",
    "ReferrerPolicy": "strict-origin-when-cross-origin",
    "ContentSecurityPolicy": "default-src 'self'",
    "PermissionsPolicy": "camera=(), microphone=()",
    "EnableHsts": true,
    "HstsMaxAgeSeconds": 31536000,
    "HstsIncludeSubDomains": true,
    "RemoveServerHeader": true
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
