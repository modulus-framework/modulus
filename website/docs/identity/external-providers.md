---
sidebar_position: 3
---

# External Providers

Modulus supports 6 external identity providers with local token validation.

## Supported Providers

| Provider | Package | Token Validation |
|----------|---------|-----------------|
| **Auth0** | `Modulus.Identity` | OIDC discovery + JWKS |
| **Authentik** | `Modulus.Identity` | OIDC discovery + JWKS |
| **Azure AD** | `Modulus.Identity` | OIDC discovery + JWKS |
| **Duende** | `Modulus.Identity` | OIDC discovery + JWKS |
| **Keycloak** | `Modulus.Identity` | RFC 7662 introspection |
| **Okta** | `Modulus.Identity` | OIDC discovery + JWKS |

## Setup

```bash
modulus app MyApp --auth auth0
```

Or add manually:

```csharp
builder.Services.AddModulusAuth0(config);
```

## Configuration

```json
{
  "Auth0": {
    "Domain": "your-tenant.auth0.com",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  }
}
```

## Token Validation

All providers (except Keycloak) validate tokens locally:

1. **Fetch JWKS** from provider's OIDC discovery endpoint
2. **Validate signature** using the provider's public keys
3. **Validate issuer** matches the discovery document
4. **Validate lifetime** with 1-minute clock skew

```csharp
// The adapter handles all validation automatically
builder.Services.AddModulusAuth0(config);

// Users are authenticated via the external provider
// ICurrentUser reflects the external user's claims
```

## Keycloak (Special Case)

Keycloak uses RFC 7662 token introspection:

```csharp
builder.Services.AddModulusKeycloak(config);
```

```json
{
  "Keycloak": {
    "Realm": "my-realm",
    "AuthServerUrl": "https://keycloak.example.com",
    "IntrospectionClient": "my-service",
    "IntrospectionSecret": "secret"
  }
}
```

## Audience Validation

Audience validation is opt-in:

```csharp
builder.Services.AddModulusAuth0(config, validAudiences: new[] { "my-api" });
```

When enabled, the token's `aud` claim must match one of the configured audiences.

## See Also

- [OpenIddict](openiddict) — Server-side configuration
