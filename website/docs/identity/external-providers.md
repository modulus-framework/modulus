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

Or add manually (extension methods on `AuthenticationBuilder`):

```csharp
builder.Services.AddAuthentication()
    .AddAuth0(builder.Configuration);
```

Config binds under `Identity:ExternalProviders:{Provider}`.

## Configuration

```json
{
  "Identity": {
    "ExternalProviders": {
      "Auth0": {
        "Authority": "https://your-tenant.auth0.com/",
        "ClientId": "your-client-id",
        "ClientSecret": "your-client-secret",
        "Scope": "openid profile email",
        "Audience": null
      }
    }
  }
}
```

Only **one** external provider per app is supported (`AllowMultipleExternalProviders`
is off by default — multiple registrations silently last-wins and are treated
as misconfiguration).

## Token Validation

All providers (except Keycloak) validate tokens locally:

1. **Fetch JWKS** from provider's OIDC discovery endpoint
2. **Validate signature** using the provider's public keys
3. **Validate issuer** matches the discovery document
4. **Validate lifetime** with 1-minute clock skew

```csharp
// The adapter handles all validation automatically
builder.Services.AddAuthentication()
    .AddAuth0(builder.Configuration);

// Users are authenticated via the external provider
// ICurrentUser reflects the external user's claims
```

## Keycloak (Special Case)

Keycloak uses RFC 7662 token introspection:

```csharp
builder.Services.AddAuthentication()
    .AddKeycloak(builder.Configuration);
```

```json
{
  "Identity": {
    "ExternalProviders": {
      "Keycloak": {
        "Authority": "https://keycloak.example.com",
        "Realm": "my-realm",
        "ClientId": "my-service",
        "ClientSecret": "secret",
        "AdminClientId": null,
        "AdminClientSecret": null,
        "Scope": "openid profile email roles"
      }
    }
  }
}
```

`AddKeycloakWithProfileFetch` (and the `AddXWithProfileFetch` variants)
additionally fetch user profiles via admin credentials.

## Audience Validation

Audience validation is opt-in via config (`Audience` above, or the provider's
`opts.Audience`) — recommended for production; off by default to avoid
rejecting valid tokens whose audience isn't the client id.

## See Also

- [OpenIddict](openiddict) — Server-side configuration
