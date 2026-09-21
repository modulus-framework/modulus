---
sidebar_position: 3
---

# CORS

Modulus provides CORS configuration with wildcard-subdomain awareness.

## Setup

Both calls are required:

```csharp
builder.Services.AddModulusCors(builder.Configuration);
app.UseModulusCors();   // between routing and authentication
```

## Configuration

```json
{
  "Cors": {
    "Enabled": true,
    "AllowedOrigins": [
      "https://app.example.com",
      "https://*.example.com"
    ],
    "AllowCredentials": true,
    "AllowedMethods": ["GET", "POST", "PUT", "DELETE"],
    "AllowedHeaders": ["*"]
  }
}
```

## Wildcard Subdomains

The CORS policy supports wildcard subdomains:

```json
{
  "Cors": {
    "AllowedOrigins": ["https://*.example.com"]
  }
}
```

This matches:
- `https://app.example.com`
- `https://admin.example.com`
- `https://tenant1.example.com`

## Security Notes

- Never combine `*` origin with `AllowCredentials`
- The single named policy is `ModulusCors`
- Call `UseModulusCors()` before auth/authorization (between routing and authentication)

## See Also

- [Security Headers](security-headers) — HTTP security
