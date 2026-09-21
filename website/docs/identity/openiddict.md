---
sidebar_position: 2
---

# OpenIddict

OpenIddict provides the OAuth 2.0 / OpenID Connect server.

## Setup

```csharp
builder.Services.AddModulusOpenIddict(config);
builder.Services.AddModulusIdentity<CatalogDbContext, AppUser, AppRole>(config);
```

`AddModulusIdentity` registers ASP.NET Core Identity (password rules: digit +
upper + length 8, unique email), the `ClaimsPrincipal`-backed `ICurrentUser`,
self-service account endpoints (see below), and replaces the deny-by-default
password-grant validator with the `SignInManager`-backed one.

## Configuration

Binds the `Identity` section:

```json
{
  "Identity": {
    "RequireConfirmedEmail": false,
    "AccessTokenLifetimeMin": 15,
    "RefreshTokenLifetimeDays": 7,
    "EnableRefreshToken": true,
    "AllowPasswordFlow": false,
    "AllowAuthorizationCodeFlow": false,
    "UseDevelopmentCertificates": false,
    "AllowMultipleExternalProviders": false,
    "AllowedPostLogoutRedirectUris": [],
    "IntrospectionClientId": null,
    "IntrospectionClientSecret": null
  }
}
```

`UseDevelopmentCertificates` in Production fails startup fast
(`DevelopmentCertificateGuard`) — register real signing/encryption
certificates via the `AddModulusOpenIddict` configure callback.

## Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/connect/token` | POST | Token endpoint (password when `AllowPasswordFlow`, refresh) |
| `/connect/authorize` | GET | Authorization endpoint (app implements; enable via `AllowAuthorizationCodeFlow`) |
| `/connect/userinfo` | GET | User info |
| `/connect/introspect` | POST | RFC 7662 introspection (caller auth via `IntrospectionClientId/Secret`, deny-by-default) |
| `/connect/revoke` | POST | RFC 7009 revocation |
| `/connect/end-session` | GET+POST | Logout (redirects only to allow-listed `AllowedPostLogoutRedirectUris`) |

Discovery (`.well-known/openid-configuration`) and JWKS are served by
OpenIddict automatically.

### Account endpoints

`AddModulusIdentity` also mounts self-service account routes:

| Endpoint | Purpose |
|----------|---------|
| `/account/forgot-password` | Request a reset token (uniform response — anti-enumeration) |
| `/account/reset-password` | Consume a reset token |
| `/account/confirm-email` | Confirm registration |
| `/account/send-confirmation-email` | Re-send confirmation |
| `/account/logout` | Sign out |

Reset/confirmation tokens are delivered through `IIdentityEmailSender` —
register your own before `AddModulusIdentity` (`TryAdd` leaves it in place);
the default is a fail-closed no-op that discards tokens rather than returning
them in API responses.

## Password Grant

Requires `AllowPasswordFlow: true` (ROPC is off by default):

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
  "expires_in": 900,
  "refresh_token": "..."
}
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
