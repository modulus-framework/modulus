---
sidebar_position: 8
---

# Secrets Guard

A startup guard that fails fast when secrets are found in committed configuration files.

## Setup

```csharp
services.AddModulusSecretsGuard(config);
```

## How It Works

Scans effective configuration at boot and flags sensitive values sourced from committed `appsettings*.json` files.

| Environment | Behavior |
|-------------|----------|
| **Development** | Fails startup on violation |
| **Staging** | Fails startup on violation |
| **Production** | Excluded (no false positive risk) |

## What's Flagged

- Connection strings with credentials (`Password=`, `AccountKey=`)
- API keys and secrets in committed config files
- Non-local connection strings

## What's Ignored

- Local/SQLite connection strings
- Values from environment variables
- Values from User Secrets
- Values from vault providers
- Non-sensitive keys

## Configuration

```json
{
  "SecretsGuard": {
    "Enabled": true,
    "FailOnViolation": true,
    "Environments": ["Development", "Staging"],
    "SensitiveKeyPatterns": ["ApiKey", "Secret", "Password", "ConnectionString"]
  }
}
```

## Fixing Violations

Move secrets out of `appsettings.json`:

```bash
# User Secrets (development)
dotnet user-secrets set "ExternalApi:ApiKey" "my-secret-key"

# Environment variables
export ExternalApi__ApiKey="my-secret-key"

# Azure Key Vault / AWS Secrets Manager
# Configure via configuration providers
```

## Template Hygiene

Generated apps include:

- `<UserSecretsId>` in host `.csproj`
- `.gitignore` covering `secrets.json` and `appsettings.*.json`
- Default `SecretsGuard` block in `appsettings.json`

## See Also

- [Feature Flags](feature-flags) — Runtime toggling
