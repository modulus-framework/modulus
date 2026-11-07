---
sidebar_position: 1
title: Backend for Frontends (BFF)
description: One gateway per client type (web, mobile, partner) on YARP, with its own authentication and edge rules.
---

# Backend for Frontends (BFF)

`Modulus.Bff` gives each kind of client its **own backend**. A BFF authenticates its callers in its own way, applies its own edge rules, proxies `/api/**` to the upstream services with YARP, and hosts aggregate endpoints shaped for that client. A BFF holds no database and loads no modules.

| Client | Authentication | Notes |
|--------|----------------|-------|
| `web` (browser SPA or server-rendered host) | Cookie session (`.bff.web`) | Tokens stay on the server. Every write needs an `X-CSRF: 1` header. |
| `mobile` (native app) | Bearer token | Enforces a minimum app version (`X-App-Version`, answers `426`). |
| `partner` (machine to machine) | Client credentials | Unsafe methods need an `Idempotency-Key` header (otherwise `400`). |

## Setup

```csharp
builder.Services.AddModulusBff(builder.Configuration, bff => bff
    .AddMobileClient()
    .AddOpenApi());

app.UseAuthentication();
app.UseModulusBff();        // per-client edge rules
app.UseAuthorization();
app.UseRateLimiter();
app.MapModulusBff();        // session endpoints and YARP routes of every client

app.MapBffClient("mobile").MapGet("/home", ...);   // aggregators behind the client's policy
```

Settings live under `Bff`:

- `Bff:Services:{name}:Address` names the upstream services. `api` is the default for a modular monolith.
- `Bff:Clients:{name}` sets the path prefix (`/web`, `/mobile`), client id and secret, scopes, rate limit and session lifetime.

Endpoints come from OIDC discovery, so the BFF works with OpenIddict, Keycloak, Auth0, Okta, Entra ID and Authentik.

## Web sessions

- Sign-in uses OIDC code + PKCE (`GET /bff/login?returnUrl=`) or the password grant (`POST /bff/login`). Return URLs must be local.
- The access and refresh tokens live server-side (`ServerSideUserTokenStore`, Data Protection encrypted). With more than one replica, add Redis and a shared key ring.
- `POST /bff/logout` revokes both tokens and clears the session.

## Aggregation

`BffComposer` runs sections concurrently, each with its own timeout and optional caching. A failed optional section is listed in `degraded`; a failed required section returns `502`.

```csharp
app.MapBffClient("mobile").MapGet("/home", async (BffComposer composer, CancellationToken ct) =>
{
    var composition = composer.Begin(TimeSpan.FromSeconds(3));
    var orders = composition.Required("orders", c => ordersApi.GetRecentAsync(c));
    var recs = composition.Optional("recommendations", c => recsApi.GetAsync(c));
    var response = await composition.ExecuteAsync(() => new HomeDto(orders.Value, recs.Value), ct);
    return Results.Ok(response);
});
```

## Command-line support

```bash
modulus app Shop --auth openiddict --bff web,mobile,partner
modulus add-bff partner                       # add a BFF to an existing app
modulus generate-bff-endpoint Dashboard --bff mobile
```

See the [CLI reference](../cli/) for all options. The API host needs `AllowClientCredentialsFlow` for partner clients, and `EncryptAccessTokens: false` when bearer clients validate tokens against the JWKS.

## Related

- [gRPC](./grpc), [GraphQL](./graphql) and [Realtime](./realtime) are routed through the same BFFs.
- [Multi-tenancy and the security model](../security/overview) describe the policies every BFF route inherits.
