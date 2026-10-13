---
sidebar_position: 5
---

# Authentication in the UI

A web app signs users in through the **BFF** (backend for frontend) pattern: the browser holds a session cookie, and the tokens for the API stay on the server. The web host is the OAuth client; the API is called with the signed-in user's access token, which the host attaches. The browser never sees the token.

Pass `--auth` to `modulus app` to pick the token server:

| `--auth` | Token server | Sign-in page does |
|---|---|---|
| `openiddict` | The API project's local OpenIddict server | Posts the user name and password to the API's token endpoint (password grant) |
| `keycloak`, `auth0`, `okta`, `azuread`, `duende`, `authentik` | The external provider | Redirects to the provider's login page (OpenID Connect code flow with PKCE) |
| `none` | None | Nothing. Pages are public, and no security guard runs. |

The three UI engines use the same BFF settings. The sign-in pages differ in their location, so that is described per engine below.

## The web host's settings

`appsettings.json` in the web project holds the client:

```json
"Bff": {
  "AuthServer": "OpenIddict",
  "Clients": {
    "web": {
      "ClientId": "shop",
      "LoginMode": "Password",
      "Scopes": [ "openid", "profile", "email", "roles", "offline_access", "modulus" ],
      "TokenStorage": "ServerSide",
      "RefreshBeforeExpiry": "00:01:00",
      "SessionLifetime": "14.00:00:00"
    }
  }
}
```

- `LoginMode` is `Password` for the local token server and `Oidc` for an external provider.
- `TokenStorage` is `ServerSide`: the tokens stay on the server, and the cookie only names the session.
- Tokens are refreshed shortly before they expire, one refresh per session at a time.
- With more than one replica, add a shared cache (`AddRedisFusionCache`) and a shared Data Protection key ring.

For an external provider, set the `Authority` under `Bff` to the provider's issuer. It must be `https` outside Development. `Bff:RequireHttpsMetadata` is `false` only in the Development settings, so a plain-HTTP issuer works on a developer machine.

## Blazor

The Blazor host keeps its sign-in pages in `Components/Pages/Account`:

| Route | Page |
|---|---|
| `/account/login` | Sign-in. With `Password`, a native form posts to `/account/login`, with an antiforgery token. With `Oidc`, the route redirects to the provider. |
| `/account/logout` | Ends the BFF session (and the provider session, when there is one) |

How it works:

- `Routes.razor` uses `AuthorizeRouteView`. A visitor who is not signed in is sent to `RedirectToLogin`, which goes to `/account/login` with the page they asked for.
- The component endpoints require sign-in (`RequireAuthorization` on the Razor components). Only the sign-in page opts out, with `[AllowAnonymous]` and a `[Loosened]` reason.
- The sign-in page renders on a blank layout (`BlankLayout`) with the framework's theme, not the admin shell.

To require sign-in on one page, add `@attribute [Authorize]`. The generated `Home` page already has it.

## MVC and Razor Pages

| Route | Page |
|---|---|
| `/Account/Login` | Sign-in |
| `/Account/Logout` | Sign-out |
| `/Account/AccessDenied` | Shown when a signed-in user lacks a permission |

Razor Pages require a signed-in user for every page under `/`, and `/Account` is open. Open another folder with `AllowAnonymousToFolder`.

## Permissions in pages and menus

- Menu items take `RequiredPolicy`. An item the user cannot use is hidden.
- A page or endpoint takes an authorization policy the same way: `[Authorize(Policy = "Customers.Read")]` in MVC, `@attribute [Authorize(Policy = "...")]` in Blazor.
- The generated app grants the example permission to the seeded `Admin` role. Grant more with `AddPermissionGrants`, and keep them in the database with `AddEfCoreAuthorizationStores` when they must be editable at run time.

## The startup security guard

The generated web host runs `AddModulusSecurityGuard` at startup. Every endpoint must resolve to a policy. An endpoint that allows anonymous access needs a reason, and outside Development it must also be in `security/loosening-allowlist.json`. Otherwise the app refuses to start.

In code, the reason is one of:

```csharp
app.MapHealthChecks("/health/live").Loosen("Liveness probe for orchestrators and load balancers");
```

```csharp
[AllowAnonymous, Loosened("Password recovery is open; the API checks the token and the account")]
```

The allowlist is a file that lists the routes the app opens on purpose:

```json
{
  "endpoints": [
    { "route": "/account/login", "reason": "Sign-in: the API checks the credentials" },
    { "route": "/health", "reason": "Liveness and readiness probes for orchestrators and load balancers" }
  ]
}
```

- `route` is the route pattern. A trailing `*` covers a prefix.
- Without `methods`, an entry covers every method.
- Framework endpoints (the sign-in pages, error pages and BFF session endpoints) carry reviewed reasons and need no entry.
- Review every change to this file as you would review a change to the authorization model.

In Development the guard reports the same findings as warnings and lets the app start. Run the host with `ASPNETCORE_ENVIRONMENT=Production` to see what would refuse to start.

## Adding the account pages

`modulus ui add-auth` adds the forgot-password and reset-password pages. They call the identity module's `/account` endpoints. `--engine blazor` picks the Blazor version.

`modulus ui add-2fa` adds a two-factor page for an authenticator app, with recovery codes and turn-off. `modulus ui add-session-manager` adds a page that lists the signed-in user's sessions, with revoke for one or all, and recent sign-ins.

## Development administrator and sample users

- With `--auth openiddict`, the API creates a development administrator on the first start and prints the account once. Set `Identity:Seed:AdminEmail` and `Identity:Seed:AdminPassword` to choose it.
- The samples in the framework repository sign in with any sample user's email and the password `modulus`.

## Production checklist

- Serve the web host and the API over HTTPS, and set `Bff:Authority` to an `https` issuer.
- Register real signing certificates for OpenIddict (the development certificates are for local use only).
- With `--auth openiddict`, the web host's sign-in uses the password grant, so `Identity:AllowPasswordFlow` must be `true` on the API for that client. The generated Production settings leave it `false`. Turn it on only for this trusted first-party web client, or use an external provider, which signs users in with the code flow instead.
- Replace the in-memory stores. At startup the app warns about in-memory stores, and `Security:InMemoryStores:Mode` set to `Fail` refuses to start while one is in use.
- Review `security/loosening-allowlist.json` and the reasons in the code.
