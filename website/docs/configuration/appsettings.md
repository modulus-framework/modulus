---
sidebar_position: 3
---

# appsettings.json

Modulus apps use a hierarchical configuration system. Sections are emitted
only for the features enabled at scaffolding time (`--enable-*` flags) —
a minimal app contains just `ConnectionStrings`, `Logging`, `OpenApi`,
`FeatureManagement`, and `AllowedHosts`.

## Structure

```json
{
  "ConnectionStrings": {
    "Catalog": "Data Source=catalog.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Correlation": {
    "HeaderName": "X-Correlation-ID",
    "IncludeInResponse": true,
    "UseTraceIdWhenMissing": true
  },
  "Idempotency": {
    "HeaderName": "Idempotency-Key",
    "Methods": ["POST", "PATCH"],
    "RequireKey": false,
    "ValidateRequestMatch": true,
    "RetentionSeconds": 86400
  },
  "OpenApi": {
    "Title": "MyApp API",
    "Version": "v1",
    "IncludeBearerSecurity": true
  },
  "ApiVersioning": {
    "DefaultVersion": "1.0",
    "AssumeDefaultVersionWhenUnspecified": true,
    "ReportApiVersions": true
  },
  "RateLimiting": {
    "Enabled": true,
    "PermitLimit": 100,
    "WindowSeconds": 60,
    "QueueLimit": 0,
    "Partition": "User"
  },
  "Cors": {
    "AllowedOrigins": [],
    "AllowedMethods": [],
    "AllowedHeaders": [],
    "AllowCredentials": false
  },
  "SecurityHeaders": {
    "ContentTypeOptions": true,
    "FrameOptions": "DENY",
    "ReferrerPolicy": "strict-origin-when-cross-origin",
    "EnableHsts": true
  },
  "FeatureManagement": {
    "SampleFeature": false
  },
  "SecretsGuard": {
    "Enabled": true,
    "FailOnViolation": true
  },
  "PersonalDataProtection": {
    "Enabled": true,
    "Purpose": "Modulus.PersonalData.Protector.v1"
  },
  "Identity": {
    "UseDevelopmentCertificates": false,
    "AccessTokenLifetimeMin": 15,
    "RefreshTokenLifetimeDays": 7
  }
}
```

Notes:

- There is no `MultiTenancy` section — tenancy is wired in code
  (`AddMultiTenancy(builder => …)`), not config.
- `Identity` uses per-provider shapes: OpenIddict emits token lifetimes as
  above; external providers emit `Identity:ExternalProviders:{Auth0,…}` with
  `Authority`/`ClientId`/`ClientSecret`/`Scope` (Keycloak uses
  `Authority`+`Realm`). Client secrets belong in User Secrets or env vars.
- There is no `OpenTelemetry` section in generated apps — telemetry is wired
  with `AddModulusOpenTelemetry(configuration, environment)`, which binds an
  `OpenTelemetry` section (`Enabled`/`ServiceName`/`EnableConsoleExporter`/
  `Otlp:{Endpoint,ExportTraces,ExportMetrics}`) when present; add it manually
  to enable OTLP export.

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
