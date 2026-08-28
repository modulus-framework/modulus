---
sidebar_position: 3
---

# CORS

Modulus provides CORS configuration with wildcard-subdomain awareness.

## Setup

```csharp
app.UseModulusCors();
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
- The wildcard policy is named `ModulusCorsPolicy`
- CORS is applied after authentication in the pipeline

## See Also

- [Security Headers](security-headers) — HTTP security
