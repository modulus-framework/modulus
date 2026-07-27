# Modulus.Identity — architecture & review

> Status: as-built (July 2026). Covers the `Modulus.Identity` package: OpenIddict
> server, ASP.NET Identity integration, the six external-IdP adapters, and the
> single-provider invariant enforced at startup.

Cross-references:

- `AGENTS.md` — top-level framework conventions and the addressed-findings log.
- `docs/architecture/authorization-framework-blueprint.md` — the broader
  authorization model (permissions, grants, resource-based checks). This doc
  covers **identity** (who you are); that one covers **authorization** (what
  you can do).

---

## 1. Scope

`Modulus.Identity` is one package, compiled from the previously-merged
`Modulus.Identity.Abstractions` + 6 standalone IdP adapters + EF Core mapping.
It provides:

- An **OpenIddict server** (authorization-code + refresh-token; ROPC opt-in).
- **ASP.NET Core Identity** integration for local users (`ModulusUser`/
  `ModulusRole` + EF Core stores).
- A `ClaimsPrincipalCurrentUser` adapter that bridges the ASP.NET `ClaimsPrincipal`
  to the framework's `ICurrentUser` abstraction.
- A **grant-store permission checker** (`AddGrantStorePermissionChecker`) for
  server-side permission resolution.
- **Six external-IdP adapters** under `ExternalProviders/`: Auth0, Authentik,
  Azure AD (Entra ID), Duende IdentityServer, Keycloak, Okta.

Out of scope (deliberately not in this package):

- Cloud-vendor SDKs (Microsoft Graph SDK, AWS Cognito SDK, etc.) — adapters
  hit REST endpoints directly to keep the dependency surface minimal.
- SAML/WS-Fed — only OIDC is supported.
- Multi-tenant isolation of the identity store itself (the framework's
  multi-tenancy applies at the data layer; the user store is shared unless
  the host partitions it).

---

## 2. The single-external-provider invariant

> **One app, one external IdP.**

A Modulus app may register **at most one** external identity provider. Calling
both `AddAuthentik(...)` and `AddOkta(...)`, for example, is rejected at
startup.

### Why this rule exists

Without an invariant, multiple `AddXxx` calls silently **last-wins** on the
scoped `IExternalIdentityProvider` service registration. The losing provider's
HTTP client and options still load, the OIDC handler still runs, but the
`IExternalIdentityProvider` view resolves to whichever was registered last.
Symptoms:

- `GetUserBySubjectAsync` calls the wrong provider's API → 404 / wrong user.
- `ValidateTokenAsync` validates against the wrong provider's JWKS → tokens
  that *should* pass fail, tokens from the wrong realm pass.
- The error surfaces deep in login, not at boot, and is nearly impossible to
  diagnose without reading the framework's source.

Multi-federation (multiple IdPs feeding one app) is a real scenario, but it
requires a deliberate design (token audience discrimination, per-issuer claim
routing, an explicit `IExternalIdentityProvider` registry). The framework does
not attempt it.

### How it's enforced

`Guards/SingleExternalProviderGuard.cs` is an `IHostedService` registered by
`AddModulusOpenIddict`. At `StartAsync` it resolves
`IEnumerable<IExternalIdentityProvider>` and throws when the count exceeds one.
The error names every registered provider so the misconfiguration is obvious:

```
Modulus allows at most ONE external identity provider per app, but 2 are
registered: 'Authentik' (Name=authentik), 'Okta' (Name=okta).
Call only one of AddAuthentik/AddAuth0/AddOkta/AddAzureAd/
AddDuendeIdentityServer/AddKeycloak.
(To bypass — unsupported, not recommended in production — set
Identity:AllowMultipleExternalProviders=true.)
```

The opt-out flag (`Identity:AllowMultipleExternalProviders`) exists for
advanced scenarios where the host app explicitly takes responsibility for
resolving the right provider per request. It is **not** supported: the
`IExternalIdentityProvider` service registration shape (single scoped
instance) cannot actually deliver per-request selection without further
infrastructure. Use it only as a short-term escape hatch.

---

## 3. OpenIddict server

`AddModulusOpenIddict(services, configuration, configure?)` wires
`ModulusTokenController` (token endpoint) + `ModulusUserInfoController`
(userinfo endpoint) via OpenIddict's AspNetCore host.

### Grants

| Grant | Default | Configuration |
|-------|---------|---------------|
| Authorization code | on | — |
| Refresh token | on | `Identity:EnableRefreshToken` |
| ROPC (password) | **off** | `Identity:AllowPasswordFlow` |

ROPC is removed in OAuth 2.1. The flag exists for first-party trusted clients
that genuinely cannot use the authorization-code flow (e.g. some CLI scenarios).

### Certificates

Development signing/encryption certificates are **off by default**. Enable in
Development only via `Identity:UseDevelopmentCertificates`. In production,
register real certificates via the `configure` callback:

```csharp
services.AddModulusOpenIddict(builder.Configuration, options =>
{
    options.AddSigningCertificate(certificate);
    options.AddEncryptionCertificate(certificate);
});
```

When both are off and no certificate is supplied, OpenIddict fails fast at
startup rather than signing tokens with throwaway keys.

### Token endpoint security

The password grant is credential-checked through `IPasswordGrantCredentialValidator`:

- `AddModulusOpenIddict` registers `NullPasswordGrantCredentialValidator`
  (deny-by-default) so the grant cannot mint tokens until a real validator
  replaces it.
- `AddModulusIdentity<TContext, TUser, TRole>` replaces the null validator
  with `IdentityPasswordGrantValidator<TUser>`, which delegates to
  `SignInManager.CheckPasswordSignInAsync` (honouring `IsActive` and lock-out).
- Granted scopes are intersected with an allow-list (`PasswordGrant.AuthorizeScopes`)
  as defence-in-depth.
- The refresh-token grant re-verifies the subject is still active before
  re-issuing, so a disabled/deleted account cannot keep refreshing.

---

## 4. External IdP design

### Contract

`Abstractions/IExternalIdentityProvider.cs`:

```csharp
public interface IExternalIdentityProvider
{
    string Name { get; }                 // "authentik", "auth0", ...
    string DisplayName { get; }          // "Authentik", "Auth0", ...
    Task<ExternalUserInfo?> GetUserBySubjectAsync(string subject, CancellationToken ct = default);
    Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default);
}
```

`ExternalUserInfo` is a normalised record mapping the provider's user shape
into a common form (`Subject`, `Email`, `UserName`, `FirstName`, `LastName`,
`AvatarUrl`, `Claims` dictionary).

### Token validation

All five OIDC adapters share `OidcDiscoveryValidator`
(`Abstractions/ExternalTokenValidator.cs`), which:

1. Fetches the provider's discovery document (fail-closed on unreachable /
   malformed discovery).
2. Builds `TokenValidationParameters` from the document's issuer + JWKS.
3. Validates **signature, issuer, lifetime (1-min skew), issuer-signing-key**.
4. Audience validation is opt-in (`validAudiences` ctor arg) — recommended for
   production.

Keycloak uses RFC 7662 introspection (its native model) and is unchanged.
Duende/Auth0/Authentik/AzureAd/Okta all use discovery + JWKS.

The pure `ExternalTokenValidator.ValidateJwtAsync` helper is unit-tested with
real RSA-signed JWTs (tampered, expired, wrong issuer, wrong audience, unknown
key).

### Per-provider extension pattern

Each adapter ships an `AddXxx(this AuthenticationBuilder, IConfiguration)`
extension that:

1. Binds the provider's options from `Identity:ExternalProviders:Xxx`.
2. Registers `services.Configure<XxxOptions>(...)`.
3. Registers `services.AddHttpClient<XxxIdentityProvider>()`.
4. Registers `services.AddScoped<IExternalIdentityProvider, XxxIdentityProvider>()`.
5. Wires the OIDC handler via `builder.AddOpenIdConnect("Xxx", ...)`.

### Discovery URLs (per provider)

| Provider | Discovery URL shape |
|----------|--------------------|
| Auth0 | `{Authority}.well-known/openid-configuration` |
| Authentik | `{Authority}application/o/{ClientId}/.well-known/openid-configuration` |
| Azure AD | `{Authority}/.well-known/openid-configuration` (Authority = `{Instance}{TenantId}/v2.0`) |
| Duende | `{Authority}/.well-known/openid-configuration` |
| Keycloak | RFC 7662 introspection (`/protocol/openid-connect/token/introspect`) |
| Okta | `{Authority}/oauth2/default/.well-known/openid-configuration` |

Authentik's per-application discovery path is unusual: each Authentik
application has its own discovery document. The `ClientId` is part of the URL,
which is correct per Authentik's docs.

---

## 5. Mental model: federated login vs delegated issuance

> ⚠️ Read this section before wiring an external IdP. The framework's external
> adapters are for **federated login**, not for delegating your token issuance.

Two patterns exist for combining an external IdP with your own OpenIddict
server, and they are not the same:

### Pattern A — Federated login (what the adapters do today)

```
Browser ──code flow──> Authentik ──code──> Browser ──> Your app
                                                       │
                                              OIDC handler (AddAuthentik)
                                                       │
                                              Sign-in cookie issued
                                                       │
                                              Inbound requests auth'd by cookie
                                              (or by tokens issued by Authentik
                                               and validated by ValidateTokenAsync)
```

In this shape, Authentik is the **identity authority**. Your app consumes
Authentik's tokens directly. Your OpenIddict server, if present, is for
issuing tokens to first-party clients that don't go through Authentik (e.g.
service-to-service within your boundary).

This is what `AddAuthentik(...)` wires. `SaveTokens = true` keeps the IdP
tokens in the cookie; `GetClaimsFromUserInfoEndpoint = true` pulls claims on
login.

### Pattern B — Delegated issuance (not what the adapters do)

```
Browser ──code flow──> Authentik ──code──> Browser ──> Your app
                                                       │
                                              OIDC handler authenticates
                                                       │
                                              OpenIddict server mints YOUR tokens
                                              (carrying claims mapped from Authentik)
                                                       │
                                              Inbound requests auth'd by YOUR tokens
```

In this shape, Authentik is the **upstream identity source**, but your app's
OpenIddict server is the token issuer for downstream consumers (your SPA,
your mobile app, your APIs). The external IdP is a step in the login flow,
not the authority your APIs trust.

The framework's `AddXxx` extensions do not implement Pattern B out of the box.
To do it you need an OIDC sign-in callback that exchanges the external
`ClaimsPrincipal` for an OpenIddict principal and re-issues tokens via
`HttpContext.SignInAsync(OpenIddictConstants.AuthenticationSchemes.Bearer, ...)`.
The token controller (`ModulusTokenController`) handles the password and
refresh grants but does not implement this external-token-exchange flow.

### What this means for users

- If you want "log in with Authentik and call my APIs with Authentik tokens" →
  Pattern A, just `AddAuthentik`. Your APIs validate tokens via
  `IExternalIdentityProvider.ValidateTokenAsync` (or via the OIDC handler's
  JWT bearer middleware).
- If you want "log in with Authentik but my APIs use tokens minted by my own
  OpenIddict server" → Pattern B, you need a custom callback. The framework
  gives you the building blocks (`AddAuthentik` for the inbound flow,
  `AddModulusOpenIddict` for your token endpoint) but does not glue them.

The review recommends a future addition: an `AddModulusFederatedLogin(...)`
helper that codifies Pattern B and ships a default external-token-exchange
controller. Tracked as a finding (§8).

---

## 6. Provider matrix

| Provider | Token validation | User fetch | Race-safe | Claim mapping | Tests | Use case |
|----------|-----------------|-----------|-----------|---------------|-------|----------|
| **Auth0** | OIDC discovery + JWKS | `/api/v2/users/{id}` (Management API, per-request token) | yes | `Name=nickname`, `Role=https://schemas.modulus.app/roles`, `MapInboundClaims=false` | (shared validator only) | SaaS apps, B2C |
| **Authentik** | OIDC discovery + JWKS (per-app discovery) | `/api/v3/core/users/{pk}/` (per-request token) | yes (fixed in this batch) | `Name=preferred_username`, `Role=groups`, `MapInboundClaims=false`, `UseTokenLifetime=true` | yes (mapping + race regression) | Self-hosted, Kubernetes-native |
| **Azure AD (Entra ID)** | OIDC discovery + JWKS | Graph API `/v1.0/users/{id}` (client-credentials token, per-request) | yes (fixed in this batch) | `Name=name`, `Role=roles`, `UseTokenLifetime=true` | (shared validator only) | Enterprise / Microsoft 365 |
| **Duende IdentityServer** | OIDC discovery + JWKS | `/connect/userinfo` (no auth — see §8) | n/a | `Name=name`, `Role=role` | (shared validator only) | Self-hosted, .NET-shop enterprise |
| **Keycloak** | RFC 7662 introspection | `/admin/realms/{realm}/users/{id}` (admin token, per-request) | yes (fixed in this batch) | `Name=preferred_username`, `Role=realm_access.roles` | (none — introspection path is untested) | Self-hosted, Java-shop enterprise |
| **Okta** | OIDC discovery + JWKS | `/api/v1/users/{id}` (`SSWS` API token, per-request) | yes (fixed in this batch) | `Name=preferred_username` | (shared validator only) | Cloud workforce + customer IAM |

"Race-safe" means the adapter does **not** write to
`HttpClient.DefaultRequestHeaders.Authorization` (which is shared across
concurrent calls and would race). All adapters now use per-request
`HttpRequestMessage` instances.

---

## 7. Wiring snippets

For each provider: install the `Modulus.Identity` package, then call one
`AddXxx`. Add **only one** — see §2.

### Authentik

```csharp
// Program.cs
builder.Services.AddAuthentication()
    .AddAuthentik(builder.Configuration);
```

```jsonc
// appsettings.json
"Identity": {
  "ExternalProviders": {
    "Authentik": {
      "Authority": "https://auth.example.com/",
      "ClientId": "modulus-app",
      "ClientSecret": "...",
      "ApiToken": "...",                       // optional; only for GetUserBySubjectAsync
      "Scope": "openid profile email groups"   // 'groups' populates role claims
    }
  }
}
```

### Auth0

```jsonc
"Identity": {
  "ExternalProviders": {
    "Auth0": {
      "Authority": "https://your-tenant.auth0.com/",
      "ClientId": "...",
      "ClientSecret": "...",
      "ManagementToken": "..."                 // optional; only for GetUserBySubjectAsync
    }
  }
}
```

### Okta

```jsonc
"Identity": {
  "ExternalProviders": {
    "Okta": {
      "Authority": "https://your-org.okta.com/",
      "ClientId": "...",
      "ClientSecret": "...",
      "ApiToken": "..."                        // optional; SSWS token
    }
  }
}
```

### Azure AD (Entra ID)

```jsonc
"Identity": {
  "ExternalProviders": {
    "AzureAd": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "...",
      "ClientId": "...",
      "ClientSecret": "..."
    }
  }
}
```

### Duende IdentityServer

```jsonc
"Identity": {
  "ExternalProviders": {
    "Duende": {
      "Authority": "https://idp.example.com/",
      "ClientId": "...",
      "ClientSecret": "...",
      "ApiName": "modulus"
    }
  }
}
```

### Keycloak

```jsonc
"Identity": {
  "ExternalProviders": {
    "Keycloak": {
      "Authority": "https://keycloak.example.com/",
      "Realm": "my-realm",
      "ClientId": "...",
      "ClientSecret": "...",
      "AdminClientId": "...",                  // optional; for GetUserBySubjectAsync
      "AdminClientSecret": "..."
    }
  }
}
```

---

## 8. Findings & remediation

| # | Finding | Severity | Status |
|---|---------|---------|--------|
| 1 | `AuthentikIdentityProvider` wrote the bearer token to `HttpClient.DefaultRequestHeaders.Authorization`, mutating shared state across concurrent calls (race) | high | **Fixed** (this batch) |
| 2 | `OktaIdentityProvider`, `AzureAdIdentityProvider`, `KeycloakIdentityProvider` had the same shared-HttpClient race | high | **Fixed** (this batch) |
| 3 | `AuthentikIdentityProvider` set no `NameClaimType` / `RoleClaimType` / `MapInboundClaims = false` on the OIDC handler, so `User.Identity.Name` and role mapping fell back to defaults that don't match Authentik's claim shape | medium | **Fixed** (this batch) |
| 4 | No XML doc on `AuthentikIdentityProvider` class (siblings had one) | low | **Fixed** (this batch) |
| 5 | Calling two `AddXxx` extensions silently last-wins on the `IExternalIdentityProvider` service — confusing wrong-provider failure deep in login | high | **Fixed** (this batch) — `SingleExternalProviderGuard` fails fast at startup |
| 6 | No provider-level unit tests for any adapter (only `ExternalTokenValidator` shared logic tested) | medium | Partly addressed — Authentik adapter covered (mapping + race regression). Others remain |
| 7 | `DuendeIdentityProvider.GetUserBySubjectAsync` calls `/connect/userinfo` with **no** `Authorization` header — the call is unauthenticated, so it returns whatever the endpoint's default policy allows (likely an error or an anonymous principal). The other adapters attach an admin/management token; Duende does not. | medium | Open. Fix is a design choice: either attach the user's access token (caller-supplied) or use client credentials against a token introspection/userinfo hybrid. Deferred for design discussion. |
| 8 | No codified Pattern B (delegated issuance — Authentik login → OpenIddict-minted tokens) | medium | Open. Future `AddModulusFederatedLogin` helper recommended. |
| 9 | `samples/Ecommerce` does not wire any external IdP, so users have no end-to-end reference for the federated-login pattern | low | Open. Recommend adding an Authentik-wired sample once Authentik is the most-requested adapter. |
| 10 | `KeycloakIdentityProvider.ValidateTokenAsync` uses RFC 7662 introspection but has no unit test for the introspection path (only OIDC-discovery providers are covered by `ExternalTokenValidatorTests`) | low | Open. |

### Things deliberately not changed

- The `IExternalIdentityProvider` service-registration shape stays
  singleton-per-provider. The single-provider invariant makes any richer
  model (keyed services, named registrations, per-request selection) unnecessary.
- The `Identity:ExternalProviders:Xxx` config section naming stays as-is —
  no auto-discovery helper. Under single-provider, calling one `AddXxx` is
  simpler than indirection through a registry.

---

## 9. Testing

- `tests/unit/Modulus.Identity.Tests/ExternalTokenValidatorTests.cs` — pure
  JWT validation logic (5 cases, real RSA keys).
- `tests/unit/Modulus.Identity.Tests/AuthentikIdentityProviderTests.cs` —
  Authentik user mapping, missing-field handling, race-safety regression,
  request-URL correctness (5 cases).
- `tests/unit/Modulus.Identity.Tests/SingleExternalProviderGuardTests.cs` —
  the guard (zero/one/two providers + opt-out flag + stop, 5 cases).
- `tests/unit/Modulus.Identity.Tests/IdentityDefaultsTests.cs` — defaults
  of `ModulusIdentityOptions`.

Integration tests for the OpenIddict token endpoint and the external OIDC
sign-in flow are not present. Standing them up requires either Testcontainers
against a real IdP or a mock OIDC authority (e.g. `IdentityServer8` test host).
Recommended as a follow-up.

---

## 10. Mental checklist before shipping an app

- [ ] Picked **one** external IdP. Did not call two `AddXxx` methods.
- [ ] Set `Identity:UseDevelopmentCertificates=false` in production and
      supplied real signing/encryption certificates via the `configure` callback.
- [ ] Did **not** set `Identity:AllowPasswordFlow=true` unless the client is
      first-party and cannot use the authorization-code flow.
- [ ] Did **not** set `Identity:AllowMultipleExternalProviders=true`.
- [ ] Restricted the `Scope` list to what your app actually needs.
- [ ] Configured audience validation on `ValidateTokenAsync` callers
      (recommended for production — see `OidcDiscoveryValidator` ctor).
- [ ] Verified the OIDC handler's claim mapping matches your provider's
      token shape (see §6 matrix).
