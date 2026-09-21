---
sidebar_position: 1
---

# Identity Overview

Modulus provides authentication and authorization via OpenIddict with support for 6 external identity providers.

## Components

| Component | Purpose |
|-----------|---------|
| **OpenIddict Server** | OAuth 2.0 / OpenID Connect provider |
| **ASP.NET Identity** | User management, password hashing, lockout |
| **External IdPs** | Auth0, Authentik, Azure AD, Duende, Keycloak, Okta |
| **Token Controller** | `/connect/token` endpoint for password + refresh grants |

## Setup

```bash
modulus app MyApp --auth openiddict
```

Or with an external provider:

```bash
modulus app MyApp --auth keycloak
```

## Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    Authentication Flow                       │
│                                                              │
│  Client ──→ POST /connect/token ──→ TokenController          │
│              (username/password)      │                      │
│                                      ├── IPasswordGrant-    │
│                                      │   CredentialValidator │
│                                      │   (SignInManager;     │
│                                      │    deny-by-default    │
│                                      │    until Identity is  │
│                                      │    registered)        │
│                                      ├── IsActive + lockout │
│                                      ├── scope intersection │
│                                      └── Return tokens      │
│                                                              │
│  Client ──→ Authorization: Bearer <token> ──→ API           │
│              │                                               │
│              └── ValidateJwt (signature, issuer, lifetime)   │
└─────────────────────────────────────────────────────────────┘
```

## Grants

| Grant | Use Case |
|-------|----------|
| **Password** | Username + password (first-party apps; opt-in via `AllowPasswordFlow` — off by default, ROPC is removed in OAuth 2.1) |
| **Refresh Token** | Token renewal (opt-out via `EnableRefreshToken: false`). Refresh rebuilds the principal from the current user store, re-checks `IsActive`/lockout/security stamp |

Any other `grant_type` → `unsupported_grant_type`. There is no
client-credentials grant.

## ICurrentUser

Inject the current user identity:

```csharp
public sealed class GetMyProfileHandler(ICurrentUser currentUser, ...)
    : IQueryHandler<GetMyProfileQuery, UserProfileDto>
{
    public async Task<UserProfileDto> HandleAsync(
        GetMyProfileQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Not authenticated");

        // currentUser.UserName / .Email / .IsAuthenticated /
        // .IsInRole(role) / .HasPermission(p) / .Permissions also available
    }
}
```

## See Also

- [OpenIddict](openiddict) — Server configuration
- [External Providers](external-providers) — IdP integration
