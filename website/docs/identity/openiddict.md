---
sidebar_position: 2
---

# OpenIddict

OpenIddict provides the OAuth 2.0 / OpenID Connect server.

## Setup

```csharp
builder.Services.AddModulusOpenIddict(config);
builder.Services.AddModulusIdentity<ApplicationUser>(config);
```

## Configuration

```json
{
  "OpenIddict": {
    "Issuer": "https://localhost:5000",
    "TokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeDays": 30
  }
}
```

## Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/connect/token` | POST | Token endpoint (password, client-credentials, refresh) |
| `/connect/authorize` | GET | Authorization endpoint |
| `/connect/logout` | POST | Logout endpoint |
| `.well-known/openid-configuration` | GET | Discovery document |
| `/jwks` | GET | JSON Web Key Set |

## Password Grant

```bash
curl -X POST http://localhost:5000/connect/token \
  -d "grant_type=password" \
  -d "username=user@example.com" \
  -d "password=secret" \
  -d "client_id=my-app"
```

Response:

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "..."
}
```

## Client Credentials

```bash
curl -X POST http://localhost:5000/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=service-a" \
  -d "client_secret=secret"
```

## Security

| Feature | Description |
|---------|-------------|
| **Password validation** | Uses `SignInManager.CheckPasswordSignInAsync` |
| **Lockout support** | Honors account lockout after failed attempts |
| **IsActive check** | Rejects inactive users |
| **Scope intersection** | Granted scopes filtered against allow-list |
| **Deny-by-default** | `NullPasswordGrantCredentialValidator` rejects all until replaced |

## See Also

- [External Providers](external-providers) — Third-party IdP integration
