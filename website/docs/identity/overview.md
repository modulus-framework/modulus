---
sidebar_position: 1
---

# Identity Overview

`Modulus.Identity` provides the sign-in server, user management and external identity for an application. It is built on OpenIddict (an OAuth 2.0 and OpenID Connect server) and ASP.NET Core Identity. Every grant and sign-in method is opt-in, and the defaults are the safer choice.

## Components

| Component | Purpose | Page |
|-----------|---------|------|
| **OpenIddict server** | Issues tokens at `/connect/token`, authorizes at `/connect/authorize`, revokes at `/connect/revoke` | [OpenIddict](./openiddict) |
| **ASP.NET Core Identity** | Users, roles, password hashing, lockout, password policy and history | [OpenIddict](./openiddict) |
| **External providers** | Sign in with Auth0, Keycloak, Azure AD, Okta, Duende or Authentik | [External providers](./external-providers) |
| **LDAP and Active Directory** | Password sign-in against a directory, with local fallback | this page |
| **SAML 2.0** | Single sign-on with an identity provider, service-provider initiated | this page |
| **Passkeys** | WebAuthn sign-in with a passkey | this page |
| **SCIM 2.0** | Provisioning of users and groups from an identity provider | this page |
| **Sessions** | Each sign-in is tracked; sessions can be listed, capped and ended | this page |

## Setup

```csharp
builder.Services.AddModulusOpenIddict(builder.Configuration);
builder.Services.AddModulusIdentity<CatalogDbContext, AppUser, AppRole>(builder.Configuration);
```

The CLI generates an identity host for you:

```bash
modulus app MyApp --auth openiddict     # local token server
modulus app MyApp --auth keycloak       # external provider (also auth0, authentik, azuread, duende, okta)
```

## Grants

Each grant is enabled under `ModulusIdentityOptions`:

| Grant or flow | Option | Default | Use |
|---------------|--------|---------|-----|
| Password | `AllowPasswordFlow` | off | First-party apps. Sign-in passes through `IPasswordGrantCredentialValidator`. Removed in OAuth 2.1, so keep it off outside development. |
| Authorization code with PKCE | `AllowAuthorizationCodeFlow` | off | Browser and mobile clients |
| Client credentials | `AllowClientCredentialsFlow` | off | Machine-to-machine (for example a partner BFF) |
| Device code (RFC 8628) | `AllowDeviceCodeFlow` | off | Devices without a browser |
| Refresh token | `EnableRefreshToken` | on | Renewing tokens. A refresh re-checks that the user is active and that the security stamp still matches. |
| Token exchange (RFC 8693) | `AddModulusTokenExchange` | off | Exchanging one token for another, for example to call a downstream service |

Any other `grant_type` is refused with `unsupported_grant_type`.

Access tokens are encrypted by default (`EncryptAccessTokens`). Set it to `false` when bearer clients or other services validate the token against the signing keys. `ValidateTokenEntries` (on by default) checks every access token against its stored entry, so a revoked token stops working at once.

## Password sign-in

- Lockout and password rules come from ASP.NET Core Identity.
- Password history and expiry are configured in the identity policy.
- An account with two-factor authentication must send a second factor (`mfa_code`) with its password. Recovery codes work once each.
- Inactive users and locked accounts are refused before any token is issued.

## External providers

The adapters for Auth0, Keycloak, Azure AD, Okta, Duende and Authentik validate tokens locally through the provider's OpenID discovery document. They check the signature, issuer and lifetime, and audience when `validAudiences` is set. See [External providers](./external-providers).

## LDAP and Active Directory

`AddModulusLdap<TUser>(configuration)` signs users in by binding to the directory as the user. Several directories can be configured. A name that the directory does not know falls back to the local password, unless `AllowLocalFallback` is off. A directory user never falls back to a local password.

## SAML 2.0

`AddModulusSaml<TContext, TUser>(configuration)` and `MapModulusSaml()` add service-provider-initiated single sign-on. Identity provider certificates are pinned in configuration. Accounts are created on first sign-in. Several providers can be configured. Single logout and identity-provider-initiated sign-in are not built.

## Passkeys

`AddModulusPasskeys<TContext, TUser>(configuration)` and `MapModulusPasskeys()` add WebAuthn passkeys, with user verification required. Passkeys are registered under the account, and sign-in uses a dedicated grant.

## SCIM 2.0

`AddModulusScim<TUser>(configuration)` and `MapModulusScim("/scim/v2")` let an identity provider create, update and deactivate users and manage roles. Requests need the `scim:manage` permission, and each client is bound to one company.

## Sessions

`ISessionRegistry` records each sign-in with its address, browser and device. `Identity:Sessions` can cap concurrent sessions per user and per role, and set idle timeouts. A user can list and end their own sessions (`GET account/sign-ins`), and an administrator can revoke all of a user's sessions through `IUserSessionService`.

## Current user

Inject `ICurrentUser` to read the identity of the caller:

```csharp
public sealed class GetMyProfileHandler(ICurrentUser currentUser)
    : IQueryHandler<GetMyProfileQuery, UserProfileDto>
{
    public Task<UserProfileDto> HandleAsync(GetMyProfileQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Not authenticated");

        // Also available: IsAuthenticated, IsInRole(role), HasPermission(permission) and Permissions.
        return Task.FromResult(new UserProfileDto(userId, currentUser.UserName, currentUser.Email));
    }
}
```

## See also

- [OpenIddict](./openiddict): server configuration
- [External providers](./external-providers): provider setup
- [Security model](../security/overview): how identity, tenancy and permissions fit together
