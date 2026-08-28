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
| **Token Controller** | `/connect/token` endpoint for password/client-credentials grants |

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
│                                      ├── ValidateCredentials│
│                                      ├── CheckPasswordSignIn│
│                                      ├── GenerateJwtToken  │
│                                      └── Return tokens     │
│                                                              │
│  Client ──→ Authorization: Bearer <token> ──→ API           │
│              │                                               │
│              └── ValidateJwt (signature, issuer, lifetime)   │
└─────────────────────────────────────────────────────────────┘
```

## Grants

| Grant | Use Case |
|-------|----------|
| **Password** | Username + password (first-party apps) |
| **Client Credentials** | Service-to-service (no user) |
| **Refresh Token** | Token renewal |

## ICurrentUser

Inject the current user identity:

```csharp
public sealed class GetMyProfileHandler(ICurrentUser currentUser)
    : IQueryHandler<GetMyProfile, UserProfileDto>
{
    public async Task<UserProfileDto> HandleAsync(
        GetMyProfile query, CancellationToken ct)
    {
        var userId = currentUser.Id
            ?? throw new UnauthorizedException("Not authenticated");

        // Fetch user profile
    }
}
```

## See Also

- [OpenIddict](openiddict) — Server configuration
- [External Providers](external-providers) — IdP integration
