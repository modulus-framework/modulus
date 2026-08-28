---
sidebar_position: 3
---

# appsettings.json

Modulus apps use a hierarchical configuration system.

## Structure

```json
{
  "ConnectionStrings": {
    "Catalog": "Data Source=catalog.db",
    "Orders": "Server=localhost;Database=Orders"
  },
  "OpenIddict": {
    "Issuer": "https://localhost:5000"
  },
  "MultiTenancy": {
    "Enabled": true,
    "Resolver": "header"
  },
  "Cors": {
    "AllowedOrigins": ["https://app.example.com"]
  },
  "RateLimiting": {
    "PermitLimit": 100,
    "WindowSeconds": 60
  },
  "Idempotency": {
    "Methods": ["POST", "PATCH"]
  },
  "Correlation": {
    "HeaderName": "X-Correlation-ID"
  },
  "SecretsGuard": {
    "Enabled": true,
    "Environments": ["Development", "Staging"]
  },
  "PersonalDataProtection": {
    "Enabled": false
  },
  "FeatureManagement": {
    "SampleFeature": false
  }
}
```

## Per-Module Connection Strings

Each module gets its own connection string key:

```json
{
  "ConnectionStrings": {
    "Catalog": "Data Source=catalog.db",
    "Orders": "Server=localhost;Database=Orders",
    "Inventory": "Host=localhost;Database=Inventory"
  }
}
```

## Environment Overrides

```json
// appsettings.Development.json
{
  "SecretsGuard": {
    "Enabled": true
  },
  "Cors": {
    "AllowedOrigins": ["https://localhost:3000"]
  }
}
```

## Environment Variables

Configuration can be overridden via environment variables:

```bash
# ConnectionStrings:Catalog → ConnectionStrings__Catalog
export ConnectionStrings__Catalog="Server=prod-db;Database=Catalog"
```

## User Secrets (Development)

```bash
dotnet user-secrets set "ExternalApi:ApiKey" "my-secret-key"
```

## See Also

- [Secrets Guard](../hardening/secrets-guard) — Startup validation
- [Build System](build-system) — Configuration hierarchy
