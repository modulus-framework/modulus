---
sidebar_position: 2
---

# OpenIddict

OpenIddict provides the OAuth 2.0 / OpenID Connect server.

## Setup

```csharp
builder.Services.AddModulusOpenIddict(builder.Configuration);
builder.Services.AddModulusIdentity<CatalogDbContext, AppUser, AppRole>(builder.Configuration);
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

The password policy (`Identity:Password`: `RequiredLength` 8, `RequireDigit` and
`RequireUppercase` on by default, `HistoryCount`, `MaxAgeDays`) and lock-out
(`Identity:Lockout`: `Enabled`, `MaxFailedAccessAttempts` 5, `DefaultLockoutMinutes` 5)
are nested sections of the same block. `ValidateTokenEntries` (default `true`) makes
every access token check its stored entry, so a revoked token stops working at once;
it needs `AddModulusIdentityStore`. `Issuer` pins the token issuer on hosts that are
reachable under more than one address.

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

Two more endpoints appear when the matching option is on: `/connect/device` and
`/connect/verify` for the device flow (`AllowDeviceCodeFlow`).

Discovery (`.well-known/openid-configuration`) and JWKS are served by
OpenIddict automatically.

### Account endpoints

`AddModulusIdentity` also mounts self-service account routes:

All are `POST` unless noted, and most of them need a signed-in caller.

| Endpoint | Purpose |
|----------|---------|
| `/account/forgot-password` | Request a reset token (uniform response — anti-enumeration) |
| `/account/reset-password` | Consume a reset token |
| `/account/accept-invitation` | Accept an invitation: set the first password and confirm the address |
| `/account/confirm-email` | Confirm registration |
| `/account/send-confirmation-email` | Re-send confirmation |
| `/account/logout` | Sign out (signed-in caller) |
| `GET /account/sessions`, `DELETE /account/sessions/{id}`, `POST /account/sessions/revoke-all` | List and revoke the caller's token sessions |
| `GET /account/sign-ins`, `DELETE /account/sign-ins/{id}` | List and end the caller's sign-ins (when `Identity:Sessions` is on) |
| `GET /account/login-history` | The caller's recent sign-in and token events (needs a security audit store) |
| `POST /account/2fa/setup`, `/2fa/enable`, `/2fa/disable` | Authenticator-app two-factor setup, enabling and disabling |

Reset, invitation and confirmation tokens are delivered through `IIdentityEmailSender` —
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
