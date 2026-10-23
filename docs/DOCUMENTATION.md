# DOCUMENTATION

This document combines 14 source documents. Contents:

- [Advanced features: hybrid cache, per-client BFFs, gRPC, webhooks, GraphQL, realtime, AI platform integration](#advanced-features-hybrid-cache-per-client-bffs-grpc-webhooks-graphql-realtime-ai-platform-integration)
- [AI connector wire contract v1 (platform-neutral)](#ai-connector-wire-contract-v1-platform-neutral)
- [App-kind split (API / Web app / Web app + API) + module UI customizability](#app-kind-split-api--web-app--web-app--api--module-ui-customizability)
- [CLI review plan: module-owned wiring, Synvro.* rename, modulus-ui package refs](#cli-review-plan-module-owned-wiring-synvro-rename-modulus-ui-package-refs)
- [ERP identity gaps: plan and status](#erp-identity-gaps-plan-and-status)
- [Production readiness: review findings and plan](#production-readiness-review-findings-and-plan)
- [Security stage 2: Company = Tenant](#security-stage-2-company--tenant)
- [Modulus Authorization & Access Control Framework — Architecture Blueprint](#modulus-authorization--access-control-framework--architecture-blueprint)
- [Modulus.Identity — architecture & review](#modulusidentity--architecture--review)
- [Dual-Targeting Plan: net8.0 + net10.0](#dual-targeting-plan-net80--net100)
- [Modulus Improvement Plan](#modulus-improvement-plan)
- [Tier 3 Roadmap — Remaining API/Platform Robustness](#tier-3-roadmap--remaining-apiplatform-robustness)
- [Database roles for tenant isolation](#database-roles-for-tenant-isolation)
- [Authorization policy combination](#authorization-policy-combination)
- [Tenant secrets, settings and navigation: plan and status](#tenant-secrets-settings-and-navigation-plan-and-status)

<!-- source: docs/ADVANCED_FEATURES_PLAN.md -->
## Advanced features: hybrid cache, per-client BFFs, gRPC, webhooks, GraphQL, realtime, AI platform integration

Roadmap for the advanced features Modulus lacked: a hybrid cache, a Backend for Frontend per client type,
gRPC, webhooks, GraphQL, realtime push and integration with a separately built AI platform.

Status: **Phase 1a (FusionCache) and phase 1b (`Modulus.Bff`, follow-ups included) are implemented and validated.**
The full solution builds with 0 warnings, every `Category=Unit` suite passes (`Modulus.Bff.Tests` 65,
`Modulus.Cli.Tests` 593) and `dotnet format --verify-no-changes` is clean. Phases 2 to 5 are done (see each
phase); phase 6 (AI platform integration) is built except `Modulus.UI.AI` (6a–6d done).
Checkboxes are updated as work lands; where the build differs from the plan, the original text is kept and
the difference is noted inline as **As built:**.

### Context

Before this work, a search of `src/` and `cli/` found no GraphQL, gRPC, YARP, BFF or hybrid cache support.

- **Query cache.** `CachingBehavior` cached `[CacheFor]` results in `IMemoryCache` only: one copy per node, no
  tag invalidation, no stampede protection.
- **Cache service.** `ICacheService` was memory-only or Redis-only, with no in-process layer in front of Redis.
  The tenant key scheme was copied by hand into three places.
- **BFF.** The only BFF-like piece was the generated `TokenRelayHandler` of the `webapp+api` Web host. It keeps
  tokens in the browser cookie, refreshes only after a `401`, has no refresh lock (rotating refresh tokens can
  race), cannot be updated in existing apps because it is generated code, and serves web clients only.

Requirements:

1. A roadmap for advanced features, implementing the first ones.
2. **Fully open-source, best-in-class packages only.**
3. A hybrid cache.
4. **A separate backend per client type** (web app, mobile app, partner).
5. Everything must work for a **modular monolith and for microservices**.
6. The BFF must work with **every auth server Modulus supports**: OpenIddict, Keycloak, Auth0, Okta, Entra ID
   (Azure AD), Duende and Authentik.

### Open-source package policy (every phase)

OSI licenses only (MIT, Apache-2.0, BSD) with no paid tier required to run; within that, the most capable package.
The policy is also recorded in `AGENTS.md`.

| Need | Chosen (license) | Rejected / note |
|------|------------------|-----------------|
| Hybrid cache | `ZiggyCreatures.FusionCache` + `.Serialization.SystemTextJson`, `.Backplane.StackExchangeRedis`, `.OpenTelemetry` (MIT) | `Microsoft.Extensions.Caching.Hybrid` alone: no backplane, fail-safe or eager refresh. FusionCache is also exposed as `HybridCache` |
| Redis L2 | `Microsoft.Extensions.Caching.StackExchangeRedis` (MIT) | |
| BFF proxy | `Yarp.ReverseProxy` (MIT) | Duende.BFF (commercial) |
| Service discovery | `Microsoft.Extensions.ServiceDiscovery` + `.Yarp` (MIT) | |
| BFF token validation | `Microsoft.AspNetCore.Authentication.JwtBearer` / `.OpenIdConnect` (MIT) | Duende IdentityServer (commercial) |
| gRPC | `Grpc.AspNetCore`, `Grpc.Net.ClientFactory`, `Grpc.HealthCheck` (Apache-2.0) | |
| GraphQL | GraphQL.NET: `GraphQL`, `.SystemTextJson`, `.DataLoader`, `.MicrosoftDI`, `GraphQL.Server.Transports.AspNetCore`, `.Ui.GraphiQL` (MIT) | HotChocolate 15/16: `HotChocolate.AspNetCore` depends on `ChilliCream.Nitro.App`, which is under the ChilliCream License 1.0 (license-key terms), not an OSI license |
| Realtime | ASP.NET Core SSE (`TypedResults.ServerSentEvents`) and SignalR (MIT, shared framework); Redis backplane on `StackExchange.Redis` (MIT); tests: `Microsoft.AspNetCore.SignalR.Client` (MIT) | |
| MCP | `ModelContextProtocol.AspNetCore` (official SDK) | |
| Tests | `FluentAssertions` **pinned to 7.x** | 8+ is commercial |

Also excluded: MediatR 13+, AutoMapper 15+, MassTransit 9+.

**As built:** the plan named `OpenIddict.Validation` for mobile token validation. It was replaced by
JwtBearer/OpenIdConnect plus a small RFC 7662 introspection handler, because OpenIddict validation is tied to
OpenIddict-shaped servers and the BFF has to work with every supported auth server.

### Topologies: modular monolith and microservices

| Feature | Modular monolith | Microservices |
|---|---|---|
| FusionCache | L1 per replica; add the Redis L2 + backplane at more than one replica | Each service has its own `CacheName` (key prefix), so shared Redis never collides; cross-service invalidation goes through integration events, never shared keys |
| BFF upstreams | One upstream (`api`) | Many upstreams (`catalog`, `orders`, ...), one YARP cluster and typed client each |
| Addressing | `Bff:Services:{name}:Address` (`Api:BaseUrl` is the fallback for `api`) | Same, or service discovery (`Bff:UseServiceDiscovery`, `https+http://catalog`) |
| Token server | OpenIddict in the API host | Any supported auth server or an identity service, reached through `Bff:Authority` and OIDC discovery |
| Aggregation | Several module endpoints on one host | Fan-out across services; per-section timeouts and degraded responses |
| gRPC (phase 2) | Optional | The main service-to-service transport |

### Roadmap

| # | Package | Status |
|---|---------|--------|
| 1a | FusionCache hybrid cache (`Modulus.Platform` + `Modulus.Caching.Redis`) | **done** |
| 1b | `Modulus.Bff`: separate backends for web, mobile and partner clients | **done** (follow-ups included) |
| 2 | `Modulus.Grpc` | **done** |
| 3 | `Modulus.Webhooks` (integration events → signed HTTP callbacks) | **done** |
| 4 | `Modulus.GraphQL` (GraphQL.NET) | **done** |
| 5 | `Modulus.Realtime` (integration events → SSE by default, SignalR opt-in) | **done** |
| 6 | AI platform integration: `Modulus.AI.Connector` (wire contract v1 inside the app) and `Modulus.UI.AI` (embedded assistant); read-only, no LLM code | **6a–6d done** (connector, index, CLI, conformance kit); `Modulus.UI.AI` deferred; see *Remaining work (phase 6)* |

---

### Phase 1a: FusionCache hybrid cache (done)

L1 memory + optional L2 Redis + Redis backplane. `Modulus.Platform` references the core FusionCache package,
Redis pieces live in `Modulus.Caching.Redis`, and `Modulus.Mediator` depends only on the `HybridCache`
abstraction.

- [x] **Shared key scheme.** `CacheKeys.Entry/Tag` (`Modulus.Core`): one tenant-scoped scheme used by the memory,
  Redis and Fusion services and by `CachingBehavior`.
- [x] **`ICacheService.GetOrCreateAsync`** as a default interface method (get, factory, set), so existing
  implementations still compile.
- [x] **`FusionCacheService`** over `IFusionCache`, with stampede protection, fail-safe, eager refresh and tags.
  **As built:** an entry whose duration exceeds `FailSafeMaxDuration` lifts the ceiling to its own duration
  (found when 8-hour BFF sessions logged a FusionCache warning).
- [x] **`AddModulusFusionCache(configuration)`** binds `Caching:Fusion` (`CacheName`, `DefaultDuration`,
  fail-safe, soft/hard factory timeouts, eager refresh, jitter, distributed-cache timeouts), registers
  FusionCache, `.AsHybridCache()`, and replaces `ICacheService`.
- [x] **`AddRedisFusionCache(configuration)`** adds the Redis L2 and backplane on one `IConnectionMultiplexer`
  (`AbortOnConnectFail=false`: a Redis outage degrades to L1). The legacy `RedisCacheService` /
  `RedisCacheBackplane` stay for existing apps.
- [x] **Observability.** `AddModulusOpenTelemetry` adds FusionCache traces and metrics
  (`OpenTelemetry:Instrumentation:FusionCache`, on by default).
- [x] **Mediator.** `[CacheFor(seconds, Tags = [...])]` goes through `HybridCache` when registered (the
  `IMemoryCache` path stays the fallback); `[InvalidatesCache(...)]` on a command removes the tags after the
  handler succeeds. `CacheInvalidationBehavior` sits outside `TransactionBehavior`, so eviction follows the commit.
- [x] **CLI.** The host template wires FusionCache (`--caching inmemory|redis`), seeds `Caching:Fusion`, and
  `generate-crud` emits `[CacheFor]` tags and `[InvalidatesCache]`.
- [x] **Tests.** `FusionCacheServiceTests` (unit: stampede, tags, tenant isolation, fail-safe; behavior tests) and
  `RedisFusionCacheTests` (Testcontainers: shared L2, backplane eviction, cache-name isolation).
  **Not run here:** the Redis integration tests need Docker.
- [x] **Fix: deleted entities served from the cache (found in phase 4).** `RemoveByTag` *expires* entries rather than
  removing them when fail-safe is on, so after a delete the handler's `NotFoundException` triggered fail-safe and the
  stale value was served (`GET` returned `200` after `DELETE` `204`, even past the TTL). The hybrid path now caches a
  `CachedOutcome<TResponse>` (key prefix `outcome:`): either the value or a captured domain error (`NotFound`,
  `Validation`, `Unauthorized`, `Forbidden` (`ForbiddenException.Permission` added), `FeatureDisabled`, `Conflict`),
  replayed to callers until the tag is invalidated. Any other exception still uses fail-safe. Covered by four new
  `FusionCacheServiceTests` and `CachedOutcomeTests`; verified live (`DELETE` `204`, then `GET` `404`).

### Phase 1b: `Modulus.Bff`, one backend per client type (done)

#### Model

One **client** per client type, mapped onto ASP.NET route groups, so two deployment styles share the code:

- **Separate deployables** (classic BFF, CLI default): `src/Bff/{App}.Bff.Web`, `.Mobile`, `.Partner`, each with
  `PathPrefix` empty.
- **One gateway host**: several clients with distinct `PathPrefix` values (`/web`, `/mobile`). **As built:**
  `MapModulusBff` throws when two clients share a prefix (their `/bff/*` endpoints would be ambiguous).

A BFF holds no database and loads no modules.

```
browser SPA ──cookie──▶ Bff.Web     ─┐
iOS/Android ──bearer──▶ Bff.Mobile  ─┼─▶ API host / services (any supported auth server)
partner     ──bearer──▶ Bff.Partner ─┘
```

#### Clients (`BffClientKind`)

| | **Web** (SPA) | **Mobile** (native app) | **Partner** (machine to machine) |
|---|---|---|---|
| Inbound auth | Cookie session (`.bff.{name}`); sign-in by OIDC code + PKCE or the password grant | Bearer: JWT against the JWKS, or RFC 7662 introspection | Bearer from client credentials (JWT or introspection) |
| Tokens upstream | Server-side store, never in the browser | Caller's bearer forwarded after client-id and scope checks | Forwarded |
| CSRF | `X-CSRF: 1` required, else `401` | none | none |
| Session endpoints | `GET /bff/login` (OIDC) or `POST /bff/login` (password), `/bff/user`, `POST /bff/logout` | `/bff/me` | `/bff/me` |
| Edge rules | — | `X-App-Version`/`X-App-Platform` gate (`426`), weak ETag + `304`, rate limit per caller (user, else client id; never a client-chosen header) | `Idempotency-Key` required on writes (`400`), rate limit |

#### Components (`src/platform/Modulus.Bff`)

- [x] **Registration.** `AddModulusBff(configuration, bff => bff.AddWebClient().AddMobileClient().AddPartnerClient().AddOpenApi())`
  binds `Bff` (`BffOptions`) and `Bff:Clients:{name}` (`BffClientOptions`). Each client gets its own scheme,
  policy `bff:{name}` (scheme + allowed client ids + required scopes) and rate-limit policy, so one client's token
  or cookie never passes another client's policy. `IBffClientContext` exposes the current client; activities are
  tagged `bff.client`.
- [x] **Every supported auth server.** `BffDiscovery` reads `.well-known/openid-configuration` (cached, with
  per-endpoint overrides); nothing is hard-coded. `BffAuthServer` (`OpenIddict`, `Keycloak`, `Auth0`, `Okta`,
  `AzureAd`, `Duende`, `Authentik`, `Generic`) only picks claim defaults. `BffClaims` normalizes client id
  (`client_id`/`azp`/`cid`/`appid`), scopes (`scope`/`scp`) and roles (`role`/`roles`/`groups`/Keycloak
  `realm_access`, plus `Bff:RoleClaimTypes`). Auth0 needs `AuthorizationParameters:audience`; Entra ID has no
  revocation or introspection, so logout skips revocation and `TokenValidation=Introspection` is refused.
- [x] **Web pieces.** `IUserTokenStore`: `ServerSideUserTokenStore` (default; `ICacheService`, Data Protection
  encrypted, keyed by the `bff_sid` claim) and `CookieUserTokenStore`. `IBffTokenClient` (password, refresh,
  revoke, userinfo; Basic auth for confidential clients). `IBffAccessTokenService` refreshes `RefreshBeforeExpiry`
  ahead of expiry under a striped per-session lock that re-reads after waiting; a refused refresh ends the session.
  `UserAccessTokenHandler` forces one refresh and replays once on a `401`.
- [x] **Mobile and partner pieces.** JwtBearer or `BffIntrospectionHandler` (results cached by token hash, capped
  at `exp`; audience checks), version gate, ETags, device partition, idempotency-key rule (`BffMiddleware`).
- [x] **Proxy.** `BffProxyConfigProvider` builds one YARP cluster per upstream service and one route per remote API,
  carrying the client's policy and rate limit. Transforms strip `Cookie`, set `X-Client-App` and the correlation id,
  and swap in the web session's token.
- [x] **Aggregation.** `BffComposer.Begin(timeout).Required(...)/.Optional(...).ExecuteAsync(shape)` returns
  `BffResponse<T>(Data, Degraded)`; sections run concurrently with their own timeouts and optional FusionCache
  caching (per user and client, with tags); a failed required section gives `502`.
  `AddBffApiClient<T>(service)` = resilient typed client with the caller's token and `X-Client-App`.
  **As built:** a composer API instead of the planned `BffEndpoint<TResponse>` base class; aggregators are minimal
  endpoints on `MapBffClient(name)`.
- [x] **Per-client OpenAPI.** `/openapi/{client}.json` via the new `AddModulusOpenApiDocument` in
  `Modulus.AspNetCore`, reusing the `AddModulusOpenApi` transformers.
- [x] **Identity options.** `ModulusIdentityOptions.EncryptAccessTokens` (set `false` so bearer BFFs validate JWTs)
  and `AllowClientCredentialsFlow` (partner clients).

#### CLI

- [x] **`modulus app ... --bff web,mobile,partner`** (needs an `--auth` provider) generates
  `src/Bff/{App}.Bff.{Client}` per client: marker `<ModulusAppKind>bff-{client}</ModulusAppKind>`, ports 5190+,
  settings with the provider's authority placeholder, an example `/home` aggregator and the `{Module}Api` typed
  client, added to the `.slnx`. The web BFF uses the code flow wherever the auth server has a login page (every
  external provider, or a `webapp` host), else the password grant.
- [x] **API host seeding (OpenIddict).** `Identity:Seed:Clients:{client}` seeded by
  `IdentitySeeding.EnsureBffClientsAsync` on every start. Web and mobile are public without a secret and confidential
  with one; partner is client credentials only, created once its secret is set through user secrets or the
  environment (committed settings keep `ClientSecret: ""`, so the secrets guard stays quiet). Bearer BFFs set
  `EncryptAccessTokens: false`, partner sets `AllowClientCredentialsFlow: true`; Development registers the redirect URIs.
  **As built:** the plan made the web client always confidential; it is public unless a secret is configured.
- [x] **`modulus add-bff <client> [--auth X]`** adds one BFF to an existing app. It detects the auth provider,
  caching provider and cross-cutting wiring from the API host, takes the next port, adds the project to the `.slnx`
  and prints the API settings to add (it does not edit them). A second run for the same client does nothing.
  **As built:** the client type is the argument (`add-bff mobile`) rather than `<Client> --kind`.
- [x] **`modulus generate-bff-endpoint <Name> --bff Mobile --modules Catalog,Orders`** (aggregator scaffold).
  **As built:** `GET /{name-in-kebab-case}` (or `--route`), one optional section per entity list of the chosen modules
  (default: every module with entities), mapped in `Program.cs` after `MapModulusBff()`. `--bff` is required when the
  app has more than one BFF; missing module clients are generated; an existing endpoint file is never overwritten.
- [x] **`webapp+api` Web host onto the BFF token pieces**, and a `modulus doctor` warning for apps still on
  `TokenRelayHandler`. **As built:** the library gained what a server-rendered host needs:
  `BffBuilder.SetDefaultClient(name)` (the client's scheme becomes the default and `IBffClientContext` falls back to it
  outside BFF endpoints), `BffClientOptions.LoginPath` / `AccessDeniedPath` (pages redirect, BFF endpoints still answer
  `401`/`403`), the public `IBffSessionService` (`SignInWithPasswordAsync`, `SignOutAsync`, now also behind
  `/bff/login` and `/bff/logout`) and `IHttpClientBuilder.AddBffUserAccessToken()`. The generated Web host uses them:
  password sign-in with the local OpenIddict server, OIDC challenge with an external provider (which the old
  password-only page could not do), server-side tokens, revocation on logout. `TokenRelayHandler.sbn` was removed.
- [x] **`ModuleDiscovery.AppInventory` learns BFF projects** (`Bffs`, by the `bff-{client}` marker), and `generate-crud`
  adds the module's typed client and the entity's methods to every BFF. **As built:** BFF typed clients moved from the
  example endpoint file to `ApiClients/{Module}Api.cs` + `ApiClients/ApiClientRegistration.cs`; they unwrap the API's
  `ApiResponse` envelope. `add-bff` generates a client for every module with entities and reuses the existing BFFs'
  services and next free port.
- [x] **`--services catalog=...,orders`** on `modulus app --bff` (and `add-bff`) for microservice upstreams: one
  `Bff:Services` entry and one `/api/{name}` passthrough route per service; a module's client uses the service named
  after it; a name without an address uses service discovery (`https+http://{name}`, `UseServiceDiscovery: true`).

#### Tests

- [x] `tests/unit/Modulus.Bff.Tests` (52): web (login, cookie holds no token, CSRF, refresh ahead of expiry, ten
  concurrent requests share one refresh, `401` → refresh → replay, refused refresh signs out, logout revokes),
  bearer (forwarding, wrong client/scope `403`, `426`, `304`, device `429`, partner idempotency, cross-client
  rejection, prefix clash), gateway and composition (degraded section, required failure `502`, timeouts, caching),
  protocol (discovery, claim normalization per server, introspection).
- [x] `BffTemplateTests` (CLI): option parsing, authority and claim defaults for all seven auth servers, login mode,
  per-client host files, API seeding and redirect URIs, `add-bff` auth detection.
- [x] CLI tests that generate real apps on disk (`BffCommandTests`): `add-bff` (next port, `.slnx`, auth detection,
  idempotency, service reuse), `generate-bff-endpoint`, a new entity reaching every BFF, and the `doctor` warning.
  `WebHostBffSessionTests` covers the Web host templates; `ServerRenderedHostTests` (library) covers a Razor-style host
  on the BFF session.

#### Verification (done)

On a generated `--kind api --auth openiddict --bff web,mobile,partner` app (0 warnings, generated tests 6/6):

- **Web:** login `200` with a 560-byte cookie and no token; no CSRF header `401`; proxied POST/GET `201`/`200`;
  `/home` aggregated; logout `204`, then `401`, revocations logged.
- **Mobile:** JWT accepted; `/bff/me`, proxy and `/home` work; `X-App-Version: 0.1` → `426`; `If-None-Match` → `304`;
  a first-party token `403`; a mobile token on the web BFF `401`; `/openapi/mobile.json` served.
- **Partner:** client-credentials token; no idempotency key `400`; a wrong secret `401`.
- A `webapp` + OIDC variant and a Keycloak variant build with 0 warnings (Keycloak tests 5/5).

Follow-ups, verified end to end:

- **`webapp+api` Web host** (`--auth openiddict`, after `generate-crud Product --module Catalog`): anonymous page `302` to
  the sign-in page, wrong password re-renders the page, login `302` with a 582-byte cookie, the admin page lists through
  the API, a product created on the page is visible through the API, a Web restart (empty token store) ends the
  session, logout revokes both tokens and the pre-logout cookie no longer works. Builds with 0 warnings; generated tests
  9/9.
- **Microservice-style BFFs** (`--bff web,mobile --services catalog=http://localhost:5180`): `generate-crud Category`
  updated both BFFs' `CatalogApi`, `generate-bff-endpoint Dashboard --bff mobile`, `add-bff partner` (port 5192, catalog
  service reused; a second run did nothing). Builds with 0 warnings; generated tests 6/6. The mobile BFF's `/dashboard`
  returned both entity lists with `degraded: []` after a create through its proxy (`201`).
- **Older bugs found and fixed on the way:** the split API host failed to start in Development (`AddModulusUi()`'s view
  resolver needs Razor's view engine, now `AddMvcCore().AddRazorViewEngine()`); the Web typed client read the API's
  `ApiResponse` envelope as the list (every CRUD page 500'd); the CRUD page called `GetAsync(cancellationToken:)` on a
  client whose parameter is `ct` (did not compile).

Not verified: the Redis/Testcontainers integration tests (no Docker here).

---

### Later phases (each gets its own detailed plan when started)

#### Phase 2: gRPC (`Modulus.Grpc`) (done)

- [x] Contract-first `.proto` files in each module's Presentation layer; service methods call `IMediator`.
  As built: `modulus generate-grpc` writes `Protos/{entity}.proto` (package `{root}.{module}.v1`, snake case) and
  `Grpc/{Entity}GrpcService.cs`, guarded by the same `Permissions(...)` as the entity's HTTP endpoints. The
  Presentation project compiles the proto with `GrpcServices="Both"`, so the test project gets client stubs through
  its Api → Presentation reference.
- [x] Exceptions mapped to gRPC status codes (`GrpcExceptionMapper` + `GrpcExceptionInterceptor`): validation →
  `InvalidArgument` with a `google.rpc.BadRequest`, not found → `NotFound`, unauthorized/forbidden →
  `Unauthenticated`/`PermissionDenied`, conflict and concurrency → `Aborted`, disabled feature → `NotFound`, anything
  else → `Internal` (no exception text unless `Grpc:EnableDetailedErrors`). Every status carries a
  `google.rpc.ErrorInfo` (`domain` = `modulus`); clients read them with `GetValidationErrors()` / `GetErrorReason()`.
  No separate tenant/correlation interceptor was needed: a gRPC call runs through the same ASP.NET Core pipeline, so
  `X-Correlation-ID` metadata, tenant resolution, authentication and the `:` permission policies already apply.
- [x] `AddModulusGrpc(configuration)` (`Grpc` section: detailed errors, reflection (off by default), health, message
  sizes) + `MapModulusGrpc(assemblies)` (maps every generated-base service, then `grpc.health.v1` and reflection,
  both anonymous). gRPC health is backed by the registered health checks, every `IModuleHealthCheck` included.
- [x] `AddModulusGrpcClient<T>` (`Grpc:Client`): gRPC's own retry policy on `Unavailable` only (a call is never
  replayed after the server acted on it), deadline/cancellation propagation inside a service, a default unary
  deadline (30 s) and the correlation id; `.PropagateTenant()` and `.ForwardAccessToken()` are opt-in.
- [x] BFFs call upstreams over gRPC: `AddBffGrpcClient<T>(service)` targets `Bff:Services:{service}:GrpcAddress`
  (else `Address`) with the caller's token (web session token or forwarded bearer) and `X-Client-App`; the 401
  refresh-and-replay is skipped for gRPC calls (their body is a stream). Use a gRPC client inside a `BffComposer`
  section like any other typed client.
- [x] CLI: `modulus generate-grpc <Entity> [--module M] [--bff mobile|web,mobile|all]` writes the contract, the service
  and `tests/{App}.Tests/{Entity}GrpcTests.cs` (round trip, 401/403 as status codes, `InvalidArgument`, `NotFound`), and
  wires the API host (`Synvro.Modulus.Grpc` reference, `AddModulusGrpc` + `MapModulusGrpc`, a `Grpc` settings
  section). With `--bff` it links the proto into each BFF as a client and registers it with `AddBffGrpcClient`.
  Idempotent: a second run changes nothing; existing files are never overwritten.

**Gotchas found end to end.**

- Kestrel does not speak h2c (HTTP/2 without TLS) on an `Http1AndHttp2` endpoint, so Development gets a second,
  HTTP/2-only endpoint (`Kestrel:Endpoints:Grpc`, `http://localhost:5189`) next to the HTTP port; the BFF's `api`
  service gets `GrpcAddress: http://localhost:5189`. With TLS, one endpoint serves both (ALPN).
- OpenIddict derives the token issuer from each request's address, so a token issued on `:5180` was rejected on
  `:5189` (`ID2088`, invalid issuer). New `Identity:Issuer` (`ModulusIdentityOptions.Issuer`) pins it;
  `generate-grpc` sets it in Development and prints a reminder to set the public URL elsewhere. Any host reachable
  under more than one address needs it.

**Verified end to end** on a generated `--kind api --auth openiddict --bff web,mobile` app: `generate-grpc Product
--bff mobile` builds with 0 warnings, the app's 11 tests pass (5 gRPC), gRPC health answers `SERVING` over h2c, an
anonymous call gets `Unauthenticated`, and a mobile token on the BFF reached the API over gRPC (200 with the product
created through the HTTP proxy). Covered by `Modulus.Grpc.Tests` (22), `Modulus.Bff.Tests` (63), `GrpcCommandTests`
and `IdentityIssuerTests`.

#### Phase 3: Webhooks (`Modulus.Webhooks`) (done)

- [x] Integration events fan out to tenant-scoped subscriptions. As built: `AddModulusWebhooks(configuration, w =>
  w.AddEvent<TEvent>(description?, payload?))` exposes an event under its `[IntegrationEventName]` and registers a
  `WebhookFanOutHandler<TEvent>` (an ordinary `IIntegrationEventHandler<T>`), so it runs wherever the event is handled:
  the in-process module bus, the outbox relay or a broker consumer (`AddEvent` also registers the type in the shared
  `IntegrationEventRegistry`, so a service that only forwards an event to webhooks still subscribes to it). The
  handler writes one `WebhookDelivery` row per matching subscription of the event's tenant (`ICurrentTenant`; host
  subscriptions have `TenantId = Guid.Empty`); a unique index on (SubscriptionId, EventId) makes a redelivered event
  a no-op. Subscriptions filter by exact name or `prefix.*` (`*` = all). The optional `payload` map keeps internal
  fields out of the body (`{"type","timestamp","data"}`).
- [x] Standard Webhooks signing (`StandardWebhooks`): `webhook-id` (`msg_{deliveryId:N}`, stable across retries, so
  receivers dedupe on it), `webhook-timestamp`, `webhook-signature` (`v1,base64(HMAC-SHA256(id.timestamp.body))`).
  Secrets are `whsec_` + base64 (24 to 64 bytes), generated on create or supplied, encrypted at rest with ASP.NET Data
  Protection. Rotation keeps the previous secret signing alongside the new one for `SecretRotationOverlap` (24 h).
  `StandardWebhooks.Verify` is public for .NET receivers (fixed-time compare, 5-minute timestamp tolerance); the spec's
  test vector is a unit test.
- [x] Delivery follows `OutboxProcessor`: candidate ids, an `ExecuteUpdate` lease claim (`LockedBy`/`LockedUntil`), a
  concurrent send (`MaxConcurrency`), then the outcome. Retries use the Standard Webhooks schedule (5 s, 5 min, 30 min,
  2 h, 5 h, 10 h, 10 h; `RetrySchedule` overrides it) and honor a longer `Retry-After` (capped at a day); after the last
  attempt the delivery is dead-lettered. `410 Gone` disables the subscription, and so does failing continuously for
  `DisableAfterFailingFor` (5 days). Delivered and dead-lettered rows are purged after `PurgeAfter` (30 days).
  `EnableLeaderElection` takes the `IDistributedLock` `modulus:webhooks:leader` before each cycle. Meter
  `Modulus.Webhooks` counts delivered, failed and dead-lettered deliveries.
- [x] SSRF guard: URLs are checked on save (absolute, https unless `AllowHttp`, no credentials or fragment, not
  loopback/private/reserved unless `AllowPrivateNetworks`), and again at connect time: the `SocketsHttpHandler`
  connect callback resolves DNS and connects only to public addresses (defeats DNS rebinding), with redirects and the
  proxy off.
- [x] Management endpoints (`MapModulusWebhooks("/api/webhooks")`, policy `webhooks:manage`, scoped to the caller's
  tenant): event types; subscriptions list/create (returns the secret once)/get/update/delete; `rotate-secret`;
  `test` (a `webhook.test` delivery); a subscription's deliveries (`?status=Pending|Delivered|Failed`); a delivery with
  its payload; `retry` (resets the attempt count). `MaxSubscriptionsPerTenant` (100) caps creation. The permission is
  declared in the registry; a host without `AddModulusAuthorization` falls back to a `permission` claim policy.
- [x] Storage: the framework ships `ModulusWebhooksDbContext` (tables `webhook_subscriptions`,
  `webhook_deliveries`, no tenant query filter: the processor works across tenants and the endpoints filter
  explicitly); `AddModulusWebhooksStore<TContext>()` also registers it as `DbContext`, so
  `MigrateModulusDatabasesAsync` and the test harness find it.
- [x] CLI: `modulus add-webhooks [--events a,b]` (refuses a `webapp` host) generates the store module
  `src/Modules/{App}.Modules.Webhooks/{App}.Modules.Webhooks.Infrastructure` (`AppWebhooksDbContext`, design-time
  factory reading `WEBHOOKS_CONNECTION`, `WebhooksModule`), wires the API host (module registration,
  `AddModulusWebhooks` with one `AddEvent` per `[IntegrationEventName]` event found in the modules'
  `Application/IntegrationEvents`, the Admin grant when the host has one, `MapModulusWebhooks`), adds settings
  (`Webhooks` + `ConnectionStrings:Webhooks`; Development allows http and private networks, Testing turns delivery
  off) and `tests/{App}.Tests/WebhookTests.cs` (401, 403, event list, subscription round trip, internal URL refused).
  Idempotent; a re-run adds events created since.

**Topologies.** Monolith: one store, the API host delivers. Microservices: each service that exposes events runs its
own store and worker (or one webhooks service consumes the events from the broker; `AddEvent` subscribes it).
Multiple replicas share the store; lease claiming prevents double sends, leader election is optional.

**Known gap.** Generated CRUD declares `{Entity}CreatedIntegrationEvent` but does not publish it: an event reaches
subscribers once a module publishes it (`IModuleBus.PublishAsync`, or a domain event implementing
`IIntegrationEvent` through the outbox).

**Verified end to end** on a generated `--kind api --auth openiddict` app: `add-webhooks` builds with 0 warnings and
the app's 11 tests pass (5 webhooks). Running it (with the create handler publishing the event): anonymous `401`, the
admin's event list, a subscription to a local receiver, a `test` delivery and a real `catalog.product-created.v1`
event both received with valid signatures and marked `Delivered`; with the receiver down the delivery stayed
`Pending` with the connection error and a 5 s retry, and `retry` delivered it once the receiver was back. Covered by
`Modulus.Webhooks.Tests` (84) and `WebhooksCommandTests` (13).

#### Phase 4: GraphQL (`Modulus.GraphQL`) (done)

**Library choice.** GraphQL.NET (MIT) instead of HotChocolate: `HotChocolate.AspNetCore` and `.Pipeline` 15/16 depend
on `ChilliCream.Nitro.App`, which is under the ChilliCream License 1.0, so HotChocolate fails the open-source policy.

- [x] **One schema from every module.** `ModulusSchema` builds `Query` and `Mutation` from the registered
  `IGraphQLContributor`s (`ConfigureQuery` / `ConfigureMutation`, default no-ops) in registration order. A duplicate
  field name fails at startup naming the contributor; a schema with no query field fails; `Mutation` is omitted when no
  contributor adds one. `AddModulusGraphQL(configuration, assemblies)` scans assemblies for public contributors and
  graph types; `AddGraphQLContributor<T>()` registers one explicitly. Another module adds fields to a type it does not
  own with `ExtendGraphType<TGraphType>(t => ...)` (the `ConfigureEntityUi` pattern).
- [x] **Resolvers through `IMediator`.** `ctx.QueryAsync(query)` / `ctx.SendAsync(command)` use the request's mediator,
  so validation, caching, transactions and logging apply as for HTTP. `ctx.LoadBatch(...)` wraps a GraphQL.NET batch
  DataLoader (one call per request for a list's extension field); `ctx.GetGuidArgument` turns a malformed id into
  `VALIDATION_FAILED`.
- [x] **Authorization.** Fields use `.AuthorizeWithPolicy("catalog:products:manage")`, the same `:` permission policies
  as the endpoints. The endpoint requires a signed-in user by default (`RequireAuthenticatedUser`), so anonymous calls
  get `401`; a missing permission is a `PERMISSION_DENIED` error.
- [x] **Errors.** `GraphQLExceptionMapper` gives `extensions.code` values matching the gRPC reasons: `VALIDATION_FAILED`
  (with `extensions.errors`), `NOT_FOUND`, `UNAUTHENTICATED`, `PERMISSION_DENIED`, `CONFLICT`, `CONCURRENCY_CONFLICT`,
  `FEATURE_DISABLED`, `CANCELLED`, `INTERNAL`, with `GlobalExceptionHandler`'s titles. Exception details only with
  `ExposeExceptionDetails`. Unhandled errors are logged (Warning for client errors).
- [x] **Limits and defaults.** Depth 15, complexity 1000 (`ListSizeEstimate` 5); introspection and the GraphiQL UI
  (`{path}/ui`) off unless configured (generated Development settings turn them on); CSRF protection on, no form
  posts, no WebSockets, batched requests off. Queries run serially unless `ParallelQueryExecution` (a scoped
  `DbContext` cannot run concurrently).
- [x] **BFFs and route groups.** `MapModulusGraphQL()` works on a route group; a BFF proxies `/graphql` to the API
  through `RemoteApis`, under the client's own auth, version gate and rate limit.
- [x] **CLI.** `modulus generate-graphql <Entity> [--module M] [--bff a,b|all]` (refuses a `webapp` host; needs the CRUD
  set) writes `GraphQL/{Entity}GraphType.cs` and `GraphQL/{Entity}GraphQL.cs` (list/get queries, create/update/delete
  mutations, the endpoints' permission) in the Presentation project, wires the API host (package, `AddModulusGraphQL`
  over the module Presentation assemblies, `MapModulusGraphQL`, `GraphQL` settings), adds
  `tests/{App}.Tests/{Entity}GraphQLTests.cs` and, with `--bff`, the `/graphql` route in each BFF. Idempotent; existing
  files are never overwritten. Templates `cli/Templates/graphql/`, wiring `GraphQLWiring`.

**Verified end to end** on a generated `--kind api --auth openiddict --bff mobile` app after
`generate-graphql Product --bff mobile`: 0 warnings, 10/10 tests. Live on the API: anonymous `401`; create, get,
update, list, delete; `NOT_FOUND` after the delete; introspection and `/graphql/ui` in Development. Through the mobile
BFF with a `gql-mobile` token: anonymous `401`, create and list `200`, `X-App-Version: 0.1.0` → `426`, a first-party
token → `403`. Covered by `Modulus.GraphQL.Tests` (28) and `GraphQLCommandTests` (11).

**Decided not to build:** subscriptions (they need WebSockets, which are off on purpose; Realtime pushes integration events with the
same auth and tenant rules, see AGENTS.md). **Not built:** cursor pagination/filtering conventions, a FusionCache-backed
response cache (queries are already cached by `[CacheFor]` through the mediator).

#### Phase 5: Realtime (`Modulus.Realtime`) (done)

**Transport choice: SSE by default, SignalR where two-way calls are needed.** Almost every push is one-way (server →
client: "a product was created", "your export is ready"), and SSE covers that with plain HTTP: it passes through the
BFFs' YARP proxy and the web BFF's cookie session unchanged, the browser's `EventSource` reconnects and resumes on its
own (`Last-Event-ID`), and native clients need no SignalR library. SignalR (`/realtime/hub`, opt-in with
`Realtime:SignalR:Enabled`) is kept for what SSE cannot do: subscribing to and leaving topics while connected,
resuming on an existing connection, and app-defined hub methods (derive from `RealtimeHub`). Both transports share
one delivery pipeline, so audiences, permissions, tenants and replay behave the same.

- [x] **Publishing.** `IRealtimePublisher.PublishAsync(type, data, audience)` (scoped; the tenant comes from
  `ICurrentTenant`, ids are `Guid.CreateVersion7` so they sort). `RealtimeAudience`: `Tenant`, `Permission(p)`,
  `User(id)`, `ForUsers(ids)`, `ForTopic(t)`, each narrowed with `.RequirePermission(p)`; always within the publishing
  tenant. Names starting with `modulus.` are reserved.
- [x] **Integration events.** `AddModulusRealtime(configuration, r => r.AddEvent<TEvent>(e => audience, payload?))`
  registers `RealtimeEventHandler<TEvent> : IIntegrationEventHandler<TEvent>` (and the type in the shared
  `IntegrationEventRegistry`), so an event raised on the module bus, the outbox relay or a broker consumer is pushed
  under its `[IntegrationEventName]`. `payload` maps what leaves the system.
- [x] **Delivery.** `IRealtimeBackplane` (in-process by default) hands every message to each node's dispatcher, which
  filters per connection (tenant, type filter, user, topic, permission) and writes to a bounded per-connection queue
  (`MaxQueuedMessagesPerConnection`; a slow client is disconnected and resumes on reconnect instead of growing memory).
  A replay buffer (`ReplayBufferSize`, `ReplayWindow`) serves `Last-Event-ID`; registration, buffering and the replay
  snapshot share one lock, so a resumed stream gets each message exactly once. An id older than the buffer gets a
  `modulus.reset` event (the client reloads its state). Duplicate redeliveries (outbox at-least-once) are dropped by id.
- [x] **Permissions.** Checked through the app's `:` authorization policies (`AddModulusAuthorization`, grant store) in
  the connection's tenant, falling back to the `permission` claim; cached per connection for
  `PermissionRecheckInterval`, so a revoked grant stops delivery within a minute.
- [x] **Topics.** `AddTopic("orders:*", permission?, authorize?)` declares what may be followed (exact names or a
  `prefix*`; the longest match wins). An unknown topic is `400` (SSE) / `HubException`; a denied one `403`.
- [x] **SSE endpoint** `GET {Path}/events?types=a,b.*&topics=x`: first event `modulus.ready` (`connectionId`, `retry`),
  then replay, then live events (`id`, `event` = type, `data` = JSON); comment-free heartbeats
  (`event: modulus.heartbeat`, ignored by `EventSource`); `X-Accel-Buffering: no`; the stream ends at the token's expiry
  (`CloseAtTokenExpiry`) so a revoked session cannot keep listening; `MaxConnectionsPerUser` → `429`.
- [x] **SignalR hub** `{Path}/hub`: client method `event` receives `RealtimeEnvelope`; `Subscribe`/`Unsubscribe`/
  `Resume(lastEventId)`; `CloseOnAuthenticationExpiration`.
- [x] **Multi-node.** `Modulus.Realtime.Redis`: `AddRedisRealtimeBackplane(configuration)` (`Realtime:Redis`,
  connection string falls back to `Caching:Redis:ConnectionString`, channel `modulus:realtime` — give each service its
  own channel). Publishing goes through Redis pub/sub and each node delivers from its own subscription (ordered,
  once); with Redis down, a node still delivers its own messages locally. In microservices, each service streams its
  own events, or one service subscribes to the others' integration events and pushes them.
- [x] **BFFs.** `RemoteApis` entries take `"EventStream": true`: the web client then lets `GET` requests with
  `Accept: text/event-stream` through without `X-CSRF` (an `EventSource` cannot set headers; a GET changes nothing),
  everything else on the route still needs it. Mobile ETags already skip event streams. YARP streams the response.
- [x] **Metrics.** Meter `Modulus.Realtime`: connections, published, delivered, dropped.
- [x] **CLI.** `modulus add-realtime [--events a,b] [--bff web,mobile|all] [--signalr]` (refuses a `webapp` host): the
  package reference, `AddModulusRealtime` with one `AddEvent` per `[IntegrationEventName]` event addressed to holders of
  the entity's CRUD permission (read from `{Entities}Endpoint.cs`; else the tenant), `MapModulusRealtime()` after the
  endpoints, the `Realtime` settings, `tests/{App}.Tests/RealtimeTests.cs` (anonymous `401`, tenant delivery, permission
  filtering through `IModuleBus`) and, with `--bff`, `/realtime` as an event stream in each BFF. Idempotent; re-run to
  add events. Templates `cli/Templates/realtime/`, wiring `RealtimeWiring`. Fixed on the way: `add-webhooks`' check for
  an already registered event looked for any `AddEvent<T>`, so a realtime registration hid it; it is now scoped to
  `webhooks.AddEvent<`.

**Verified end to end** on a generated `--kind api --auth openiddict --bff web,mobile` app after
`add-realtime --bff all --signalr`: 0 warnings, 9/9 tests. With `CreateProductHandler` publishing
`ProductCreatedIntegrationEvent`: on the API, anonymous `401` and an Admin token received `modulus.ready` then the
event. Mobile BFF: old app version `426`, otherwise the stream works and a create (`201`) arrives on it. Web BFF:
cookie-only stream works (no CSRF header), a plain GET or a create without CSRF `401`, a create with CSRF arrives;
heartbeats pass through YARP; `Last-Event-ID` through the web BFF replayed exactly the two missed events. Hub
negotiate `200` with a token, `401` anonymous. Covered by `Modulus.Realtime.Tests` (31 unit), the BFF event-stream test
and `RealtimeCommandTests` (9).

**Not run here:** the Redis two-node tests (`RedisRealtimeBackplaneTests`, `Category=Integration`: order, exactly once,
channel isolation) need Docker. **Not built:** mobile push notifications (APNs/FCM) for disconnected apps, a SignalR
backplane for the hub's own group features (not used: delivery goes through the realtime backplane), presence.
**Resolved:** generated Create handlers publish `{Entity}CreatedIntegrationEvent` through `IModuleBus` after the commit (`CreateHandler.sbn`).

#### Phase 6: AI platform integration (`Modulus.AI.*`)

Replaces the earlier one-line "MCP" phase.

##### Context

The AI platform is a **separate product** with its own document set (`E:\Personal\framework\ai-platform`, v6.5:
01 BRS, 02 Architecture, 03 Integration Guide, ADRs AD-01 to AD-28). It owns:

- LLMs, model routing, the planner, the Policy Enforcement Point (PEP), RAG and `pgvector`, the grounding
  validator, conversations and metering;
- the UI SDKs.

It stays application-agnostic (AD-01). So Modulus holds **no LLM code, prompts or provider SDKs**. Its job is to
make any Modulus app a first-class participant in the platform's published **wire contract v1** (BRS §7.2,
Integration Guide §9), in both directions:

- **Data source:** a Modulus app hosts the connector endpoints *inside the app* and passes the platform's
  conformance suite like any connector (AD-02: no privileged path).
- **Host:** a Modulus UI embeds the assistant through `AiPlatform.Sdk.AspNetCore` and mints session tokens
  server-side (Integration Guide §7).

Rule: **AI proposes and orchestrates; Modulus validates, authorizes and executes.**

Platform rules this phase must follow, and how they change the first draft of this plan:

| Platform rule | Consequence for Modulus |
|---|---|
| Read-only; writes have no design (BRS §1.4, Architecture §20) | Only `IQuery<T>` requests become capabilities. Commands, proposals and confirmations wait for the platform's write decision (6e). |
| Models hold no tools or MCP (AD-19, FR-18b) | No MCP server. Capabilities go into the manifest, and the platform's planner and executor call them. |
| API keys between app and platform, plus a platform-signed envelope for the user (AD-25, FR-27, FR-39a) | No token exchange. Modulus checks the API key the platform presents, verifies the JWS envelope (~60 s) and runs the request **as the asserted user**, with that user's own Modulus permissions. |
| Webhooks are hints, never data (AD-27, SEC-16) | Change notifications carry ids only. Data always flows through `/changes` and `/resources:get`. |
| Scope caching only in the platform; revocation is mandatory (AD-12, AD-13, FR-17, FR-19a) | The authorization adapter never caches. Every permission change calls `POST /revocations/scope` at least once. |
| Classification from the manifest: Public / Internal / Confidential / Restricted (BRS §6.2) | Map `ModulusTaxonomy` onto it (6a). `Secret` never appears in the manifest. |
| Authoritative figures come from a deterministic app query (FR-04, AD-11) | Totals and counts are `Calculate`-style capabilities backed by queries, never by text the model reads. |
| No direct database access or free-form SQL (FR-20) | No SQL tool. Everything goes through mediator queries and the repository. |

What already exists in Modulus and is reused:

- mediator queries and their pipeline (`[RequirePermission]`, `[RequireFeature]`, validation, caching);
- `IPermissionRegistry`, `IPermissionChecker.GetEffectivePermissions()` and the grant store;
- field masking (`Authorization/Fields`) and the `ModulusTaxonomy` attributes;
- `ISecurityAuditLog`, the outbox (at-least-once), and `Modulus.Webhooks` signing (HMAC-SHA256);
- `ISlotContributor`, `ViewData.SetPageId`, the resilient HTTP client, and `SecurityProbeSuite`.

Gaps:

- There is no registry of request types.
- `EntityUiSchema` covers extension fields only.
- `EntityChange` has no query API.
- Nothing calls an external endpoint when a grant, role or membership changes.

##### Split of responsibility (the AI vision against the two products)

| Vision item | Lives in | Modulus work |
|---|---|---|
| Chat, planner, LLMs, RAG, embeddings, grounding, citations, conversation state, SDK UI, settings components, metering | AI platform | none |
| Assistant, NL search, NL report, global search, "everything about PO-123" | platform (planning) + Modulus (data) | capabilities, manifest, extraction and authorization endpoints (6a, 6b) |
| Context awareness, ERP help | platform | manifest descriptions and deep links (6b); open question for the platform: page/record context from the host (6e) |
| Audit assistant, root-cause evidence chain | platform + Modulus | history and audit capabilities (6b) |
| Embedding the assistant in Modulus pages | platform SDK + Modulus | `Modulus.UI.AI` (6c) |
| NL → ERP action, form assist, procurement/BOM suggestions | **not yet**: the platform is read-only | 6e, after the platform's write decision |
| Anomalies, forecasts, proactive notifications | **not yet**: no request-less actions in the platform | 6e |
| Document/invoice extraction (OCR) | **not yet**: not in the platform's scope | 6e |
| SQL assistant | **never against the database** (FR-20) | none |
| AI CRUD / module generator (developer AI) | a separate developer tool, not the platform | `modulus describe --json` and `--json` on the generators (6c) |
| AI report / workflow / form / permission designers | prerequisite | Modulus has no report, workflow or form engine; build those first. **Out of scope.** |

##### Packages

- **`Modulus.AI.Connector`**: wire contract v1 hosted inside a Modulus app. It holds the capability registry,
  manifest, endpoints, authorization adapter, envelope verification and revocation client.
  **Decision:** it implements wire contract v1 from the platform's published OpenAPI spec (`Integrations.Contracts`)
  and takes **no package dependency on the platform**. That keeps the open-source dependency policy intact
  whatever the platform's packages are licensed under. It also means neither product's release cycle blocks the
  other: the contract is versioned by major (AD-10), and Modulus declares which majors it supports.
  Compatibility is proven by the platform's conformance suite (6d), not by sharing code.
- **`Modulus.UI.AI`**: also has no platform package dependency. It provides the session-token endpoint (a resilient
  `HttpClient` call to the platform's `/v1/session-tokens`) and a slot that renders the SDK's framework-agnostic
  `<ai-assistant>` web component. The app supplies the SDK script: vendored or self-hosted, so the CSP stays clean.
  An app that prefers the platform's `AiPlatform.Sdk.AspNetCore` can use it directly instead.

##### 6a: Connector foundation

**Built** (2026-10-03): `src/ai/Modulus.AI.Connector`, tests in `tests/unit/Modulus.AI.Connector.Tests` (47). Wiring:

```csharp
builder.Services.AddModulusAiConnector(builder.Configuration, ai => ai.UseIdentityUsers<AppUser>(u => u.IsActive));
...
app.UseAuthentication();
app.UseAuthorization();
app.MapModulusAiConnector();   // /_ai/connector/*, excluded from OpenAPI
```

- [x] **Capability registry** (`Capabilities/AiCapabilityRegistry.cs`).
  - `[AiCapability(name, description, ResourceType = ...)]` and `[AiResource(type, description, DeepLink, TitleField)]`
    live in Core (`Modulus.Core.Abstractions.Ai`), so modules need no connector reference. Opt-in only.
  - Candidates: the request types of every registered `IQueryHandler<,>` / `ICommandHandler<,>` (so whatever
    `AddMediatorHandlers` registered), plus `AddCapability<T>()` / `AddCapabilitiesFrom(assemblies)`. Read when the
    registry is first resolved; a startup check builds it and the manifest, so a bad declaration stops the host.
  - Refused at startup: an annotated command (the platform is read-only), a type that is not an `IQuery<T>`, a name
    not shaped `A.B.C[.D...]`, a duplicate, a description over `MaxDescriptionLength`, a capability `ResourceType`
    with no lookup, a lookup without a single `Guid`/`string`/`int`/`long` constructor, a missing `TitleField`.
  - Input schema: .NET 10 `JsonSchemaExporter` (`[Description]` honoured), **no new dependency** instead of
    `Microsoft.Extensions.AI.Abstractions`. Unknown argument members are refused (`INVALID_REQUEST`).
- [x] **Manifest** (`GET /manifest`, API key only). Capabilities (all `readOnly`), required permissions, resource types
  (deep-link template, title field) and the output fields of each result type with their class. Fingerprint =
  first 32 hex chars of SHA-256 over the manifest JSON, so it only changes when the manifest does.
  - Classification mapping as in the table above, plus `[Classified(FieldClassification)]` (the field-masking
    attribute); the strictest attribute wins. `[SecretData]` fields are absent from the manifest and every answer.
  - **Changed from the plan:** fields come from the query's result type (DTO), not from a new
    `IEntityMetadataRegistry`. The DTO is what is actually served, so the manifest cannot list a field the app never
    returns. Extension fields (`ExtraProperties`) are not merged yet.
- [x] **Execution** (`POST /capabilities/{name}:execute`, `POST /resources:get`).
  - Runs through `IMediator` as the envelope's user, inside the instance's company (`VerifyTenantAsync` +
    `EnterTenant` in the same frame; never the host context), under `CallTimeout`.
  - Fields the user may not read (`IFieldAuthorizer`; without one, a fail-closed authorizer with an empty registry)
    are dropped before serialization, at every nesting level. `MaxResults` caps a result (`truncated: true`).
    Deep links get `PublicBaseUrl` when relative.
  - Errors: `DENIED` (403; also unauthenticated 401), `NOT_FOUND` (404), `UNAVAILABLE` (503, timeouts and unexpected
    failures, no exception text), and **`INVALID_REQUEST`** (400, malformed body or arguments). `INVALID_REQUEST` is
    not in the platform's list: raise it with the platform contract (or map it to `DENIED`). `RATE_LIMITED` is left
    to the app's rate limiter.
- [x] **Identity** (`Security/`).
  - Two schemes: `ModulusAiConnector.Service` (API key only: manifest, health) and `ModulusAiConnector` (API key +
    envelope: everything else), each with its own policy (no `:`, so not a permission policy).
  - API key: `Authorization: ApiKey <key>`, compared in constant time against 1–2 SHA-256 hashes
    (`AiApiKeys.Hash`); a request with a browser `Origin` is refused.
  - Envelope (`AiPlatform-Envelope` header, `Microsoft.IdentityModel.JsonWebTokens`, MIT; pinned 8.16.0, the
    version OpenIddict already pulls): RS/PS/ES signature against an inline JWKS and/or a JWKS URL (refreshed hourly,
    forced at most once a minute on an unknown key id), issuer, audience = a configured instance, lifetime + skew,
    `exp - iat` ≤ `MaxEnvelopeLifetime`, `jti` and `iat` required, `tenant_id` + `app_instance_id` claims, replay
    refused.
  - Instance mapping: `Ai:Connector:Instances` (`AppInstanceId`, `PlatformTenantId`, `TenantId` = the company),
    instead of the planned `Ai:Connector:Tenants`.
  - User: `IAiConnectorUserResolver` (`UseIdentityUsers<TUser>(isActive)` by id/e-mail/user name with a lock-out
    check, or `UseUserResolver<T>`). The default refuses everyone and logs a startup warning. The principal carries
    `sub`, NameIdentifier, `role`s, `tid` and the `ai_*` claims, so the app's own `ICurrentUser` and grant-store
    checker answer for it unchanged. Membership is checked (`RequireMembership`, default on).
  - Every refusal is `401 DENIED`; the reason goes to the security audit only.
- [x] **Authorization adapter** (`POST /authz/scope`, `/authz/resources:check`, `/authz/fields:check`).
  - Scope: roles, `ICurrentUser.Permissions`, data scopes (`company`, `orgUnits` from `ICurrentDataScope`), field
    policies (`{resourceType or capability}.{field}`: Allow/Deny, secret fields absent), `ScopeTtl` (≤ 5 min) and
    the revocation key. Nothing is cached (AD-13).
  - Resources check runs each record's lookup as the user (≤ `MaxBatchSize`); denied or missing = `false`.
- [x] **Revocation client** (`Revocation/AiRevocation.cs`).
  - New Core hook `IAccessChangeObserver` (`AccessChange`: kind, reason, company, user), called by `TenantManager`
    (membership added/removed, company activated/deactivated) and by the authorization admin API (grants, roles, org
    units, placements, feature entitlements, delegations) after their audit event.
  - The connector's observer queues one signal per affected instance; `AiRevocationDispatcher` posts
    `{ revocationKey, appInstanceId, reason, occurredAt }` to `{Platform:BaseUrl}/revocations/scope` with
    `Platform:ApiKey`, retrying the same payload with jittered exponential back-off (cap `RevocationMaxBackoff`)
    until a 2xx (AD-12).
  - The revocation key is per instance (`modulus:{appInstanceId}`), deliberately coarse: a role grant reaches users
    no per-user key would name.
- [x] **Audit and guard.** Every call is recorded (category `ai`; actor, instance, envelope id, capability or
  target, outcome, error code, correlation id; never data). Every route carries a policy, so the startup guard sees
  them as policed.

Known limits of 6a (follow-ups):

- The revocation queue is **in memory**: a signal pending at shutdown is lost (bounded by the platform's 5-minute
  scope TTL). Moving it to the outbox needs an outbox-capable store in the host.
- The envelope replay cache is **per process**; with several replicas a replay could reach another node within the
  envelope's ~60 s.
- Identity (`ModulusUserManager`: role add/remove, lock-out, disable, delete) now notifies observers. Not hooked yet:
  grant-store writes made outside the admin API.
- ~~`SecurityProbeSuite` does not cover `/_ai/connector/*`~~: done in 6c (`ProbeScheme`; API-key endpoints expect `401`, or `403` on a multi-tenant host).
- No OpenAPI spec of the contract exists yet; the wire shapes in `Contract/WireContract.cs` follow Architecture
  §4/§19 and should be regenerated from `Integrations.Contracts` when it is published (6d).

##### 6b: Data for answers and the index

**Built** (2026-10-04):
- The connector parts are in `src/ai/Modulus.AI.Connector`, with 98 tests in `tests/unit/Modulus.AI.Connector.Tests`.
- The new package is `src/ai/Modulus.AI.Connector.EntityFrameworkCore`, with 28 SQLite tests in
  `tests/unit/Modulus.AI.Connector.EntityFrameworkCore.Tests`.

Wiring:

```csharp
builder.Services.AddEntityChangeHistory();                 // only for the history capability
builder.Services.AddModulusAiConnector(builder.Configuration, ai => ai
    .UseIdentityUsers<AppUser>(u => u.IsActive)
    .UseEntityFrameworkCore()                              // journal, change feed, entity source, purge
    .AddAuditCapabilities()                                // Modulus.Audit.Log.Search (needs an IAuditLogStore)
    .AddEntityChangeHistoryCapability());                  // Modulus.Audit.EntityChange.List
```

- [x] **Extraction** (`GET /extract?appInstanceId=&resourceType=&cursor=&limit=`, `GET /changes?appInstanceId=&since=&limit=`).
  - **Opting in.** Put `[AiIndexed("Module.Entity")]` (from Core) on an entity. The startup check refuses the entity
    unless both hold:
    - the resource type has an `[AiResource]` lookup;
    - the entity's `Id` type matches the lookup's id type.
  - **Who reads.** Ingestion has no user.
    - The platform authenticates with its API key only and names the instance in `appInstanceId`. That instance must
      be configured, or the call gets `403` and is audited.
    - The connector then runs as the **indexing identity** (`AiIndexer`) in the instance's company. Its roles are
      `Ai:Connector:Indexing:Roles` (default `AiIndexer`), optionally with a `ServiceUserId`.
    - The app's grant store decides what the index may hold, so grant that role the entities' read permissions.
  - **Extract.**
    - Indexed types come in name order and keys in key order. Paging is keyset; the cursor is base64url
      `{type, last key}`.
    - Each key is read through the type's lookup as the indexing identity. Masks, the company filter and soft delete
      therefore apply exactly as they do for a user call.
    - A record the identity cannot see is skipped.
    - A type it may not read at all fails the call with `DENIED`. That way a missing grant is noticed, rather than
      silently producing an empty index.
    - Each record carries `access`: the lookup's permission and the `company` data scope.
  - **Journal.**
    - `UseEntityFrameworkCore()` maps `{prefix}ai_changes` into **every** `ModuleDbContext` through the new
      `IModuleModelContributor` seam. Each row (`AiChangeRecord`) holds a sequence, tenant, resource type, id, kind
      and time.
    - A new `IModuleSaveContributor` seam runs in `ModuleDbContext.SaveChangesAsync`: after audit fields and
      soft-delete conversion, before the outbox.
    - The contributor adds one row per added, modified or deleted indexed entity **in the same `SaveChanges`**, so the
      row commits or rolls back with the entity.
    - A soft delete is journaled as a delete.
    - The tenant comes from `IHasTenantId`, else the ambient company, else `Guid.Empty`.
  - **Changes.**
    - Each context numbers its own rows, so the cursor holds one sequence per context: base64url JSON, keyed by the
      context's type name.
    - For each context, the feed reads up to `limit + 1` rows of the instance's company.
    - It cuts them at the first row newer than `now − ChangesSettleDelay` (default 5 s), because a lower sequence may
      still be committing.
    - It merges the contexts oldest first and returns only the last change of each record.
    - An upsert is re-read through the lookup. A record the indexing identity can no longer see becomes a
      **tombstone**.
    - A foreign cursor gets `INVALID_REQUEST`.
  - **Retention.**
    - `AiChangeJournalPurgeService` deletes rows older than `AiChangeJournalOptions.Retention` (30 days), every
      `PurgeInterval` (hourly), in the host context.
    - The platform must read `/changes` more often than the retention period, or re-extract.
    - The page of `/changes` and `/extract` is capped by `Indexing:PageSize` (100). An unsettled row does not set
      `hasMore`, so the platform does not poll in a loop.
- [x] **Change hints.**
  - Settings live in `Ai:Connector:Indexing:ChangeHints`: `Enabled`, `Interval` (30 s) and `Path`
    (`/webhooks/app-changes`).
  - The secret is `Ai:Connector:Platform:WebhookSecret`: `whsec_` plus base64 of at least 16 bytes. It is checked at
    startup when hints are on.
  - `AiChangeHintService` polls each instance's journal head. When the head moves, it posts
    `{ appInstanceId, eventId, occurredAt }` (no data, AD-27).
  - Each post carries the platform API key and the Standard Webhooks headers:
    - `webhook-id`: `hint_…`
    - `webhook-timestamp`
    - `webhook-signature`: `v1,<HMAC-SHA256 of id.ts.body>`
  - **Changed from the plan:**
    - It uses its own signer of a few lines. `Modulus.Webhooks` is a store-backed delivery system the connector should
      not depend on.
    - It polls the head rather than using a save hook, so a hint never sits inside a transaction.
- [x] **Search capabilities.**
  - Put `[AiQueryable("Module.Entity", description, permission, Fields = [...])]` (from Core) on an entity. That
    generates `{ResourceType}.Search` and `{ResourceType}.Calculate`, both requiring `permission`.
  - Only the listed scalar properties (never `[SecretData]`) can be filtered, sorted, grouped or aggregated. Only they,
    plus the key, are returned.
  - Operators are `eq ne gt ge lt le contains startsWith in isNull isNotNull`, each checked against the field's type.
  - Limits:
    - at most `MaxFilters` filters;
    - at most 3 sort keys;
    - at most 100 `in` values;
    - 1 to 200 characters for `contains` and `startsWith`.
  - **Changed from the plan:** filters are built directly as `Expression<Func<T,bool>>`, not as `ISpecification<T>`.
    Each value is bound through a closure member, so it is sent as a query parameter. There is no SQL or LINQ text.
  - A filter, sort or group on a field the caller cannot read gets `DENIED`, because filtering on a masked field would
    reveal it.
  - Results go through the same projector as other capabilities: masks, `MaxResults` and `truncated`.
  - `Calculate`:
    - supports `count`, `sum`, `average`, `min` and `max`;
    - can group by one field;
    - returns rows `{ group, value }`, largest first, capped at `MaxGroups`.
  - `EfAiEntitySource` runs these queries through the module context that maps the entity, untracked, with its query
    filters (company, soft delete). It also serves the keyset reads of `/extract`.
- [x] **History and audit capabilities.** Both are gated by `audit:view` and return pages of 1 to 100 rows (default 20).
  - **`Modulus.Audit.Log.Search`** (`AddAuditCapabilities`) runs over `IAuditLogStore`. It always searches the call's
    own company. With no company in scope, it returns nothing.
  - **`Modulus.Audit.EntityChange.List`** (`AddEntityChangeHistoryCapability`) uses the new
    `IEntityChangeHistoryReader.QueryAsync(EntityChangeQuery)`, registered by `AddEntityChangeHistory()`.
    - The reader reads every context that maps `EntityChange`, newest first, in the current company.
    - Only `[AiIndexed]` and `[AiQueryable]` entities are reachable.
    - A value is null when its property is unknown or `[SecretData]`.
    - A value is also null when it is classified (`[Classified]` or a compliance attribute) and the caller's field mask
      cannot read it.
    - Without an `IFieldAuthorizer`, every classified value is hidden.
    - A bug found while testing is fixed: values of `[Classified]` properties used to be returned to any `audit:view`
      caller.

Known limits of 6b (follow-ups):

- **N+1 reads.** `/extract` and `/changes` read each record through its lookup query, so each page costs N+1 queries.
  `Indexing:PageSize` (default 100) bounds this. A batch lookup would need a second attribute shape.
- **Keys.** `[AiIndexed]` entities need a **client-generated key** (`Guid.CreateVersion7()`). A store-generated key
  fails the save with a clear message, rather than journaling a wrong id. Composite keys are not supported.
- **Migration.** `ai_changes` is a new table in every module context. **Add a migration**
  (`modulus migrate add AiChanges`), or rely on `EnsureCreated` in development.
- **Hint delivery.** Hints are sent **at most once** per head move, and are lost while the platform is down. The
  platform's own `/changes` schedule is the safety net.
- **Personal data.** `[PersonalInformation]` fields are **not masked per user** in capability results.
  - Masking follows `[Classified]` through `IFieldAuthorizer`.
  - Personal fields are declared `Restricted` in the manifest, so the platform keeps them out of embedded text
    (AD-06).
  - If some users must not see a personal field, classify it with `[Classified]` too.
- **Aggregates.** An ungrouped aggregate groups by a constant, so there is one query shape. The SQLite tests cover
  decimal and double sums and averages. Other providers are not run in CI for these shapes.
- **Decided not to build (2026-10-09).** `Search` over extension fields (`ExtraProperties`) and `ISearchContributor`.
  - `ExtraProperties` is a `Dictionary` value-converted to JSON text. A provider-agnostic filter would need `EF.Functions.Like` on the serialized
    text, but EF cannot apply a string function to a converted property, and the JSON operators differ per provider. A correct version needs a schema
    change (a generated search column, or a JSON-mapped column per provider), which is a migration for every existing deployment.
  - The extension-field definitions and their per-field permissions live in `Modulus.UI.Core`, which the connector cannot reference.
  - Filtering in memory after a bounded fetch would give wrong, truncated answers, so it is not offered.
  - Cross-entity search has no consumer yet; the platform already searches per type through `{Type}.Search`.
  - Revisit when a deployment needs it: add a searchable column per declared key, not a JSON scan.

##### 6c: Host integration, UI and CLI

- [x] **`Modulus.UI.AI`, built as `modulus ui add-assistant`** (scaffolded into the Web app, not a package: `Ai/AiAssistant.cs`,
  `Pages/Shared/_AiAssistant.cshtml`, `Ai:Host` settings; Razor Pages and MVC layouts). The policy check runs only when the app
  defines the `ai:use` policy; otherwise sign-in gates it. The design:
  - `POST /ai/session` (behind the sign-in) mints the platform session token with the **host API key**, which is
    held server-side only (`Ai:Host:ApiKey`, user secrets or a vault; covered by the secrets guard).
  - An `ISlotContributor` renders the SDK's assistant view component, permission-gated (`ai:use`) and
    feature-gated (`Ai`).
  - The SDK talks to the platform gateway directly with its DPoP-bound short-lived token. It does **not** go
    through the BFF, and it stores nothing in the browser (FR-41a).
  - The SDK assets are vendored or self-hosted so the CSP stays clean. Settings components for tenant admins
    (FR-40c) mount on an admin page. They need the platform's separate tenant-admin token, never the end-user
    session.
  - In a `webapp+api` split, the session endpoint and the assistant live in the Web host, and the connector lives
    in the API host.
- [x] **CLI (connector side).**
  - `modulus add-ai` (`AddAiCommand`, wiring in `AiWiring`; refuses a `webapp` host):
    - adds `Synvro.Modulus.AI.Connector` and `.EntityFrameworkCore` to the API host;
    - wires `AddModulusAiConnector(..., ai => ai.UseIdentityUsers<ModulusUser>(user => user.IsActive).UseEntityFrameworkCore())`
      (no user resolver when the host has no local accounts; it warns) and `MapModulusAiConnector()` after the
      endpoints;
    - writes `Ai:Connector` to `appsettings.json` with `Enabled: false`, so validation is skipped and nothing can call
      the connector until the app is registered with the platform;
    - writes `appsettings.Testing.json` with a test key hash, a throwaway platform (`.invalid`) and one instance
      (`test-instance`);
    - writes `tests/{App}.Tests/AiConnectorTests.cs`: no key `401`, a signed-in user `401`, a key sent with `Origin`
      `401`, and the platform reads the manifest.
    - It is idempotent. The `--connector` / `--host` switches were dropped with the UI package.
  - `generate-crud <Entity> --ai`:
    - the entity gets `[AiIndexed("Module.Entity")]` and `[AiQueryable(..., Fields = [scalar properties])]`;
    - `Get{Entities}Query` gets `[AiCapability("{App}.Module.Entity.List", ...)]`;
    - `Get{Entity}ByIdQuery` gets `[AiResource(..., TitleField = first string property)]`;
    - both queries get `[RequirePermission]` with the CRUD permission when the host has permissions, because a
      capability runs through the mediator, not behind the HTTP endpoint;
    - Program.cs grants the permission to the `AiIndexer` role;
    - with `AiConnectorTests.cs` present it writes `{Entity}AiCapabilityTests` (manifest entries; a created record
      appears in `/extract` when the entity is not tenant-owned and has a `Name`; an unknown instance gets `403`);
    - it warns when the connector is not wired yet.
  - **`modulus describe [--json]`**: the app's name, kind, hosts, modules (provider, migration engine, migrations,
    entities), BFFs and wired features.
  - **Global `--json`**: the human output is silenced and prompts are off. The command ends with one JSON document on
    stdout: `success`, `exitCode`, `dryRun`, `files` (`path`, `action`: created/updated; csproj edits and ejected
    views included), `error`, `result`. Together with `--dry-run`, this lets a developer-AI tool plan and run the
    generators instead of re-implementing the templates.
  - **Probe suite.** `SecurityProbeSuite` now expects `401` on the signed-in probes for an endpoint whose policies
    accept only other authentication schemes (the connector's API-key endpoints): the test user cannot authenticate
    there at all. `SecurityProbeOptions.ProbeScheme` names the test scheme.
  - Verified end to end on a generated `--kind api --auth openiddict` app, built from freshly packed packages:
    `add-ai` then `generate-crud Product --module Catalog --ai`. It built with 0 warnings and 14/14 tests passed
    (SecurityProbeTests included). An `add-ai --json` rerun reported `files: []`. Covered by `AiWiringTests` and
    `AiCommandTests`.
  - Known limits:
    - `generate-crud --ai` marks only what it finds on disk (an older CRUD set is marked too).
    - The `ai_changes` journal needs a migration (`modulus migrate add AiChanges`); `add-ai` says so.

##### 6d: Conformance

- [x] **Inside Modulus (built).** The kit is the package `Modulus.AI.Connector.Testing` (no platform package). It has
  two parts:
  - `AiFakePlatform` is the fake platform. It signs envelopes with its own RSA key. A `signedByStranger` envelope and
    an unsigned one are also available. It receives revocation signals on the connector's internal HTTP clients and
    records every call, and `FailNext` makes it refuse to test retries. `platform.Configure(services)` points the
    connector at it. That covers the API-key hash, issuer, keys, base URL, two instances (one per company), no settle
    delay and no change hints.
  - `AiConnectorConformance.RunAsync(services, client, platform, options)` runs the platform's categories
    (Architecture §9.3, Integration Guide §14.1) and returns an `AiConformanceReport` (`EnsurePassed()` throws with
    every failure). The categories are:
    - **health**;
    - **authentication**: a valid envelope is accepted, and each of these is denied: no API key, an unknown key, a
      browser origin, no envelope, unsigned, signed by an unknown key, expired, another issuer, a lifetime that is
      too long, no `jti`, an unknown user and a replay;
    - **tenant isolation**: denied for an instance the app does not serve, an instance paired with another platform
      tenant, an envelope addressed to another instance, and extraction for an unknown instance. The scope is the
      envelope's instance and company for at most five minutes, and another instance never gets this company;
    - **manifest**: contract version, unique names, schemas, sensitivity;
    - **deny paths**: an unknown capability or resource type is `NOT_FOUND`, a malformed body or non-object
      arguments are `INVALID_REQUEST`, and a missing record is never returned;
    - **query injection (FR-26a)**: undeclared arguments are `400`, and SQL/DAX/comment/wildcard values come back as
      data or a typed refusal, never a `5xx`;
    - **field security**: capability results and records hold only declared fields the user may read, and field
      checks return only the requested fields, consistent with the scope;
    - **batch authorization**: order kept, empty batch, oversized batch is `400`;
    - **extraction**: paging without repeats, invalid cursor/type/limit, change-feed cursor resumes, tombstones
      well-formed;
    - **revocation (AD-12)**: a simulated access change reaches `/revocations/scope` with the scope's key, and
      retries resend the same payload;
    - **no adapter-side caching (AD-13)**: the scope after `ChangeUserAccess` differs, or the user is refused;
    - **typed errors**: every refusal carries a typed code.

    Checks that need a user report `NotApplicable` without `AiConformanceOptions.User`.
- In-repo coverage: `ConformanceTests` in `Modulus.AI.Connector.Tests`. A correct connector passes every category,
  and the suite catches each planted defect: no revocation observer, a caching user resolver, and a search that
  breaks on a quote.
- Generated coverage: `modulus add-ai` also writes `AiConformanceTests`, and the test project references the package.
  The test creates a local account (`Admin` when the host has it) and, on a multi-tenant host, a company with that
  account as a member. The access change removes the role, or deactivates the account.
- **Defects the suite found on generated apps (fixed):**
  - `IdentityAiConnectorUserResolver` looked accounts up outside the host context. The identity store's tenant
    filter then hid every account, so on a `--multi-tenancy` host every user call was refused. It now enters the
    host context for the lookup.
  - The EF change feed returned an empty cursor for an empty journal. It now always returns an encoded position
    (`e30`).
  - `SecurityProbeSuite`'s foreign-tenant probe expected `401` on the connector's API-key endpoints. A multi-tenant
    host's tenant middleware answers `403` first, and both are now accepted.
- Verified end to end from freshly packed packages: `--kind api --auth openiddict` passed 15/15, and the same with
  `--multi-tenancy` passed 16/16, both after `add-ai` and `generate-crud Product --ai`.
- Timeout-as-deny runs only when the app names a deliberately slow capability (`AiConformanceOptions.SlowCapability`,
  category `TimeoutIsDeny`); without one that check reports `NotApplicable`. Response shapes against the platform's
  published OpenAPI spec are not covered (the spec is not yet published).
- [ ] **Against the real platform.** Run the platform's `AiPlatform.Integrations.Conformance` suite against a
  generated app, in the platform's or the app's CI, before a connector is activated. The in-repo kit mirrors its
  categories but does not replace it.

##### 6e: Waiting on platform decisions (not designed here)

These need a platform BRS decision first (Architecture §20). Modulus will follow, not lead:

- **Write actions.** If writes come, the likely shape is: the platform returns a proposal, and the **user confirms
  and Modulus executes** the `ICommand` under the user's own session (preview → confirm → execute → verify →
  audit). That shape enables NL → action, form assist, and draft requisitions and BOMs.
- **Proactive insights** (anomalies, forecasts, alerts without a user request). If the platform adds them,
  Modulus would receive them as notifications (`INotificationPublisher`, Realtime).
- **Document extraction (OCR).** If the platform adds it, Modulus would provide files through presigned URLs and
  take the extracted data back as a write proposal.
- **Host context.** The current page, entity and record passed from the SDK to the planner as a hint (never as
  authority).
- **MCP or other agent access.** Excluded by AD-19 for the platform. A separate MCP adapter for other agents is
  not planned.

##### Remaining work (phase 6)

Everything still open, by owner. 6a–6d are built (2026-10-04). The items below are not started.

**Waiting on the UI change**
- [ ] `Modulus.UI.AI`: the assistant host, as designed under 6c (session endpoint, slot contributor, `ai:use`
  gate, `Ai` feature flag, a CLI wiring step).
- [ ] Verification step 3: a web app shows the assistant, and its session endpoint refuses anonymous callers.

**Waiting on the platform**
- [ ] Run the platform's `AiPlatform.Integrations.Conformance` suite against a generated app before activation (6d).
- [ ] Regenerate the wire shapes (`Contract/WireContract.cs`) from the published `Integrations.Contracts` /
  OpenAPI spec, and add response-shape checks to the conformance kit.
- [ ] Every 6e item: write actions (proposal → user confirms → Modulus executes), proactive insights, document
  extraction, host page/record context.

**Modulus-side follow-ups (no blocker; pick up when needed)**
- [x] Access-change hooks for Identity: `ModulusUserManager<TUser>` (registered by `AddModulusIdentity`) notifies on role
  add/remove, lock-out, disable and delete (`UserManagerAccessChangeTests`).
- [x] Access-change hook for grant-store writes made outside the admin API: `EfPermissionGrantStore` notifies the observers itself (scoped observers are resolved in a short scope), so seeders, jobs and app code are covered; the admin API reports its change too, which only repeats an idempotent signal (`GrantStoreAccessChangeTests`).
- [x] Durable revocation queue: `Ai:Connector:Platform:RevocationSpoolFile` journals pending signals and resends them after a restart (a file, not the outbox; one per process).
- [x] Shared envelope replay cache: with an `IDistributedLock` registered (Redis) the first node to see an envelope id takes a lease that lasts until it expires.
- [x] Batch record lookup for `/extract` and `/changes`: optional `[AiResource(BatchLookup = typeof(...))]`, one query per page (`generate-crud --ai` emits it).
- [x] `Search` over extension fields (`ExtraProperties`) and an `ISearchContributor`: decided not to build (see 6b notes).
- [x] Per-user masking of `[PersonalInformation]`/`[ProtectedPersonalData]` fields: opt in with `Ai:Connector:MaskPersonalInformation` (Restricted clearance of the field-security profile; fail closed without one).
- [x] Composite keys for `[AiIndexed]` entities (`AiCompositeKey`; up to 4 columns). Store-generated keys stay unsupported by design: the id is unknown when the journal row is written in the same save, so keys are generated on the client.
- [x] At-least-once change hints while the process lives (a refused hint is resent on the next check; `/changes` polling remains the safety net across restarts).
- [~] Aggregate, search, date-filter, keyset-paging and composite-key shapes on PostgreSQL, SQL Server and MySQL: `AiEntitySourceProviderTests` (`tests/integration/Modulus.EFCore.Integration.Tests`, `Category=Integration`) is written and compiles, but has **not been run yet** (no working container runtime when it was written); run it with `DOCKER_HOST=... dotnet test --filter AiEntitySource` and fix any provider-specific translation failure it finds.
- [x] A timeout-as-deny check in the conformance kit (`AiConformanceOptions.SlowCapability`, category `TimeoutIsDeny`).
- [ ] `generate-crud --ai` on an older CRUD set and the `ai_changes` migration stay manual steps (`add-ai` prints them).

##### Critical files

- `src/messaging/Modulus.Mediator/Attributes/PipelineAttributes.cs`,
  `src/messaging/Modulus.Mediator/Extensions/MediatorServiceCollectionExtensions.cs`
- `src/core/Modulus.Core/Abstractions/Permissions/IPermissionRegistry.cs`,
  `src/platform/Modulus.Platform/Authorization/` (`Grants/`, `Fields/`, `Governance/Delegation.cs`)
- `src/core/Modulus.Core/Abstractions/Compliance/ModulusTaxonomy.cs`
- `src/core/Modulus.Core/Abstractions/Security/SecurityAudit.cs`
- `src/data/Modulus.EntityFrameworkCore/` (`ModuleDbContext` save hook, `ChangeHistory/`)
- `src/platform/Modulus.Platform/AuditLogging/IAuditLogStore.cs`
- `src/platform/Modulus.Platform/MultiTenancy/` (`TenantManager`)
- `src/messaging/Modulus.Outbox/`, `src/messaging/Modulus.Webhooks/` (signing)
- `src/ui/Modulus.UI.Core/Entities/EntityUiSchema.cs`, `src/ui/Modulus.UI.Core/Contributors/Slots.cs`
- `src/testing/Modulus.Testing/` (`SecurityProbeSuite`)

##### Tests

- **Registry.** Only `[AiCapability]` queries are exposed, a command is refused, and the schemas and manifest
  classifications match the mapping table (`Secret` is absent).
- **Envelope.** A bad signature, an expired envelope, a replay, a wrong audience or app instance, an unknown user,
  and a company without membership are all refused.
- **Execution.** A user without the permission gets `DENIED`, masked fields are absent from the JSON, and the
  tenant filter holds.
- **Authorization adapter.** Answers match the grant store, nothing is cached, and a timeout gives `UNAVAILABLE`.
- **Revocation.** A grant change produces exactly one acknowledged call after retries (fake platform), and the call
  is idempotent.
- **Extraction.** The cursor resumes, tombstones appear for deletes, and the journal is written in the same
  transaction (6b: `IndexingAndQueryTests`, `ChangeJournalTests`, `EntitySourceTests`,
  `EntityHistoryCapabilityTests`, `AuditCapabilityTests`).

##### Verification

1. Generate an app: `modulus app --kind api --auth openiddict`, then `add-ai`, then
   `generate-crud Product --ai`. It must build with 0 warnings and the generated tests must pass (done).
2. The conformance kit (`AiConformanceTests`, fake platform) passes against it (done: 15/15, and 16/16 with
   `--multi-tenancy`). The platform's own suite is still to run.
3. (Deferred with `Modulus.UI.AI`.) A web app shows the assistant to a signed-in user with `ai:use`, and its session endpoint
   refuses anonymous callers.

### Verification checklist (every phase)

1. `dotnet build modulus.slnx`: 0 warnings, 0 errors (PublicAPI analyzers included).
2. `dotnet test modulus.slnx --filter "Category=Unit"`; integration tests with Docker where they exist.
3. `dotnet format modulus.slnx --verify-no-changes`; `dotnet list modulus.slnx package --vulnerable`; license of
   every new package checked against the policy table.
4. End to end on an app generated from freshly packed packages.

<!-- source: docs/AI_CONNECTOR_CONTRACT.md -->
## AI connector wire contract v1 (platform-neutral)

This document describes the HTTP contract that an application exposes so an external AI platform can use it. It is
written so that **any stack** (not only Modulus) can implement it. `Modulus.AI.Connector` is the reference
implementation; the shapes below come from `src/ai/Modulus.AI.Connector/Contract/WireContract.cs`, and where the two
differ the source wins.

### Principles

- **The app holds no AI logic.** Chat, planning, LLMs, embeddings, vector storage and retrieval belong to the platform.
  The app exposes data and read-only capabilities, nothing else.
- **Read-only.** A capability never changes data. Commands are not exposed.
- **Every answer is authorized as the user.** The app runs each call as the user the platform asserts, inside that
  user's company (tenant), with the app's own permission and field rules. The platform never decides access.
- **No caching of access decisions in the app; short caching in the platform** (scope TTL is at most 5 minutes) with
  push revocation.
- **Fail closed.** Any authentication problem is `401` with code `DENIED`; the reason goes to the app's audit trail,
  never to the caller.

### Transport and encoding

- JSON, `camelCase`, enums as their names, nulls written (a null is an answer), unknown response members ignored (a
  later minor version of the same major may add some).
- Capability **arguments** are strict: an unknown member is refused (`INVALID_REQUEST`).
- Base path is configurable (Modulus default `/_ai/connector`). The contract is versioned by **major** (`"1"`).
- Calls carrying a browser `Origin` header are refused.

### Authentication

| Call kind | Header | Meaning |
|---|---|---|
| Platform itself (`manifest`, `health`, `extract`, `changes`) | `Authorization: ApiKey <key>` | The app stores only SHA-256 hashes of at most two keys (rotation without downtime). |
| On behalf of a user (everything else) | `Authorization: ApiKey <key>` **and** `AiPlatform-Envelope: <jwt>` | The envelope is a short-lived JWT signed by the platform. |

Envelope rules (all must hold, else `DENIED`):

- Signed with an asymmetric algorithm (RSA, RSA-PSS or ECDSA) against the platform's published JWKS; issuer matches.
- `aud` contains the `app_instance_id` claim; the instance must be mapped to that `tenant_id` in the app.
- Has `jti` and `iat`; `exp - iat` is capped (default 5 min; the platform issues about 60 s); clock skew about 30 s.
- `jti` was not seen before (replay protection). It is checked per process by default; with an `IDistributedLock` registered
  (Redis in a multi-replica deployment) it is checked cluster-wide.
- Claims read: `sub` (the user, configurable), `email`, `tenant_id`, `app_instance_id`, `correlation_id`.
- The user resolves to an active account in the app; when the instance maps to a company, the company is active and
  the user is a member.

### Endpoints

| Method and path | Auth | Purpose |
|---|---|---|
| `GET /manifest` | key | What the app offers (below). |
| `GET /health` | key | `{ "status": "ok", "contractVersion": "1" }` |
| `POST /capabilities/{name}:execute` | key + envelope | Run a capability. Body `{ "args": { ... } }`. Answer `CapabilityResult`. |
| `POST /resources:get` | key + envelope | Body `{ "resourceType", "resourceId" }`. Answer `AppResource` (only fields the user may see). |
| `POST /authz/scope` | key + envelope | What the user may do: `AccessScope`. |
| `POST /authz/resources:check` | key + envelope | Body `{ "resources": [ResourceReference] }` (batch capped, default 100). Answer decisions in request order. |
| `POST /authz/fields:check` | key + envelope | Body `{ "resource", "fields": [..] }`. Answer `{ "allowedFields": [..] }`. |
| `GET /extract?appInstanceId&resourceType&cursor&limit` | key (service identity) | Paged full extraction for indexing. |
| `GET /changes?appInstanceId&since&limit` | key (service identity) | Incremental changes after a cursor. |

`extract` and `changes` run as a per-instance **indexing identity** (a role in the instance's company), not as a user.
An unconfigured `appInstanceId` is `403`.

### Shapes

```text
ConnectorManifest   { contractVersion, appType, appName, fingerprint, capabilities[], resourceTypes[] }
ManifestCapability  { name, description, readOnly:true, inputSchema(JSON Schema), requiredPermissions[],
                      resourceType?, outputFields[] }
ManifestResourceType{ type, description, deepLink?, titleField?, indexed, fields[] }
ManifestField       { name, type, classification }        // secret fields are never listed
ResourceReference   { resourceType, resourceId, namespace? }
AppResource         { reference, fields{name: json}, deepLink }
CapabilityResult    { resources[AppResource], truncated }
AccessScope         { appInstanceId, roles[], permissions[], dataScopes{k: string[]},
                      fieldPolicies{ name: "Allow"|"Deny" }, ttlSeconds, revocationKey }
IndexedResource     { reference, fields{}, deepLink, access:{ requiredPermissions[], dataScopes{} } }
ExtractPage         { resources[IndexedResource], nextCursor? }   // null cursor = complete
ResourceChange      { kind:"Upsert"|"Tombstone", reference, resource?, occurredAt }
ChangesPage         { changes[], cursor, hasMore }                 // pass cursor back as `since`
```

- `fingerprint` changes exactly when what the platform sees changes, so a deploy that changes nothing visible needs no
  new approval.
- Field `classification` is one of the platform data classes. A field with no declared class is treated as
  **Confidential**, never public.
- Deep links are relative unless the app has a public base URL.

### Errors

`{ "code": "...", "message": "..." }`

| Code | Meaning | Platform behavior |
|---|---|---|
| `DENIED` | Not allowed, or any authentication failure (`401`/`403`) | Final, never retried |
| `NOT_FOUND` | No such capability, resource type or record, or the user may not know it exists | Final |
| `UNAVAILABLE` | Timeout or failing dependency (default call timeout 10 s) | Retried |
| `RATE_LIMITED` | Too many calls | Retried with back-off |
| `INVALID_REQUEST` | Arguments do not match the input schema | Treated as a deny |

### Indexing and RAG

The platform builds its own embeddings and vector index from `/extract` and `/changes`. The app guarantees:

1. **Complete, resumable extraction** by opaque cursor.
2. **Deletes and loss of visibility arrive as `Tombstone`** changes (deleted, soft-deleted, or no longer visible to the
   indexing identity), so the index does not keep removed or re-scoped data.
3. **Change journal written in the same transaction as the data**, so a change cannot be lost between commit and
   journal. Changes younger than a settle delay (default 5 s) are withheld so an earlier, still-committing sequence
   number is not skipped.
4. **Access metadata per record** (`access.requiredPermissions`, `access.dataScopes`) for platform-side pre-filtering.
5. **Optional change hint**: a signed `{ appInstanceId, eventId, occurredAt }` POST (Standard Webhooks signature) so the
   platform polls `/changes` sooner. A hint never carries data.

**Retrieval rule for platform implementers:** pre-filtering by `access` is an optimization only. Before a retrieved
record is used in an answer, re-authorize it with `/authz/resources:check` (or fetch it with `/resources:get`), and mask
fields with `/authz/fields:check`. The index may be stale or over-inclusive; the app is the authority.

### Revocation

When access changes (grants, roles, memberships, accounts), the app POSTs to the platform's `/revocations/scope`:

```text
RevocationSignal { revocationKey, appInstanceId, reason, occurredAt }
```

The same payload is used on every retry. The platform must drop cached scopes for that `revocationKey`.

### Conformance

A conformant app passes these categories: health, authentication, tenant isolation, manifest, deny paths, query
injection, field security, batch authorization, extraction, revocation, no adapter-side caching, typed errors, and
timeout-as-deny (`TimeoutIsDeny`, which applies only when the app names a slow capability to test). In
Modulus run them with `AiConnectorConformance.RunAsync` (package `Modulus.AI.Connector.Testing`). Other stacks can
reimplement the same checks against this document.

### Not in this contract

- Writes or commands (read-only by design).
- MCP, or any agent protocol. These are adapters over this contract, not part of it. A separate adapter that exposes
  the manifest's capabilities as MCP tools and its resource types as MCP resources, for agents that are not the
  platform, is not built and `docs/ADVANCED_FEATURES_PLAN.md` currently lists it as not planned. If it is ever added
  it must reuse the same per-user authorization and audit path.

<!-- source: docs/APP_KIND_SPLIT_AND_IDENTITY_UI_PLAN.md -->
## App-kind split (API / Web app / Web app + API) + module UI customizability

Implementation plan for two independent efforts:

1. Replace the current two-value `api`/`web` app kind with a genuine three-way split, where
   `webapp+api` is **two separately deployable projects** that talk over HTTP (not one process).
2. Make **every** prebuilt feature UI package (Identity, Users, Tenancy, Permissions, Settings,
   AuditLogging, Notifications, Files) and every CLI-generated business-module UI as customizable
   as the CRUD scaffolding already is — extension fields, new pages, per-page styling — not just
   Identity/Account.

Status: **Phase A implemented and validated** — all three kinds (`api`, `web`/`webapp`, `webapp+api`)
build 0 warnings and pass their generated test suites off the current CLI (9/9, 6/6, 3/3
respectively). **Phase B is superseded (2026-10-11):** the server-rendered UI it targets (`src/ui/`) was removed from
this repository, and module UI customizability now belongs to the `modulus-ui` repository. Its open boxes are not
tracked here any more. Checkmarks below are updated as work lands; as-built
deviations from the plan are noted inline as **As built:** — original plan text kept for context.

### Context

Today `modulus app --kind api|web` supports two kinds, but `web` bundles Razor Pages UI *and*
API endpoints into **one process** (`src/API/{App}.Api`) — pages call `IMediator` in-process,
there's no HTTP hop. The target is a genuine three-way split instead:

1. **`api`** — API only (unchanged).
2. **`webapp`** — Razor Pages only; stays a single in-process host (no exposed `/api` surface),
   pages still call `IMediator` in-process. This is the cheap option, not a network-isolated shell.
3. **`webapp+api`** — **two separately deployable projects**. The webapp has *no* module/DbContext
   references at all and calls the API over HTTP via generated typed clients, replacing in-process
   `IMediator` calls. This must be a real HTTP split, not a shared-process convenience wrapper.

Module UI customizability gap: an audit of `Modulus.UI.Identity`, then a second full audit of the
other 7 prebuilt feature UI packages plus CLI `generate-crud --with-ui` output, confirmed the
customizability gap is **not** confined to Identity — see the summary table under Phase B. Both
audits read actual source (PageModels, `.cshtml`, entities, DbContexts), not just AGENTS.md prose.

#### Non-goal: no full microservices scaffolding

Cross-checked the three-way split against a generic "modular monolith → BFF split → microservices"
progression (Razor Pages monolith / separate API+Web / per-service-DB microservices behind a
gateway). `webapp` maps to the first, `webapp+api` maps to the second. The third — per-module
independently deployable services, each with its own database and `.sln`, wired through an API
gateway (YARP/Ocelot) and async messaging across process/repo boundaries — is **explicitly not a
target of this plan or of `modulus app`**. Modulus is a modular-monolith framework by design
(AGENTS.md, "Project"); it already ships the building blocks a team would hand-assemble to go that
far itself (`Modulus.Sagas`, `Modulus.Outbox`/`Modulus.Inbox`, `EventBus.RabbitMQ`/`EventBus.Kafka`,
per-module `DbContext`s that are already schema-isolated), but generating a gateway, per-service
solutions, or k8s manifests is out of scope. Recorded here so a future planning pass doesn't
re-propose a fourth `AppKind` for it without first re-deriving this boundary.

Key correction found during planning: `ModulusAccountController`
(`src/identity/Modulus.Identity/ModulusAccountController.cs`) **already exposes**
`POST /account/forgot-password`, `/reset-password`, `/confirm-email` server-side, with correct
anti-enumeration behavior. The gap is UI pages only, not identity/token plumbing — this
significantly shrinks the scope of Phase B1 below.

### Sequencing

1. Phase A0–A1: rework the app-kind model + inventory, add `api`/`webapp` (still single-project,
   low risk, mostly mechanical).
2. Phase A2: `webapp+api` two-project split with generated HTTP clients — highest risk/effort,
   build once A0–A1 is proven.
3. Phase A3–A5: propagate the kind model through every other CLI command; backward compat;
   two-process testing story.
4. Phase B0–B7: prebuilt & CLI-generated module UI customizability. Independent of Phase A except
   that B1's Forgot/Reset pages get an HTTP-calling variant once A2 exists (fast-follow, not
   duplicated work). B0 (extra-properties plumbing) must land before B2 (form wiring).

---

### Phase A — Three-way app-kind split

#### A0. `AppKind` model + inventory rework (foundation)

- [x] A0.1. `cli/Services/AppKind.cs` — replace the 2-value enum with 3: `Api`, `WebApp`,
      `WebAppApi`. `Names = ["api", "webapp", "webapp+api"]`.
- [x] A0.2. Keep `Parse` accepting the legacy `"web"` as a **deprecated alias** mapping to
      `WebApp` (backward compat, see A4).
- [x] A0.3. `cli/Services/ModuleDiscovery.cs` — `AppInventory` currently exposes one
      `ApiProjectPath`/`ProgramCsPath`. Added parallel `WebProjectPath`/`WebProgramCsPath` (nullable).
- [x] A0.4. Added convenience properties so call sites can migrate with a one-line swap:
      - `UiProjectPath => Kind == AppKind.WebAppApi ? WebProjectPath : ApiProjectPath`
      - `UiProgramCsPath => Kind == AppKind.WebAppApi ? WebProgramCsPath : ProgramCsPath`
- [x] A0.5. For `webapp+api`, Kind is read from the API csproj (source of truth); Web csproj
      carries matching `<ModulusAppKind>` for self-description only (Phase A2 will populate).
- [x] A0.6. Audited call sites: ~25 references across GenerateCrudCommand, DoctorCommand,
      InfoCommand, UiEject, UiAddCommand, UiInfoCommand, UiUpdateCommand, UiRemoveCommand,
      UiSearchCommand, UiListCommand. All use ApiProjectPath/ProgramCsPath; migration follows A1.
- [x] A0.7. Added webapp+api fixture test to AppKindTests verifying convenience properties fallback
      correctly when WebProjectPath is not yet populated.

#### A1. `api` / `webapp` kinds (single project, smallest delta) ✅ COMPLETE

`webapp` = today's `web` minus the exposed API surface. Single project, pages still call
`IMediator` in-process.

- [x] A1.1. `cli/Templates/app/Program.sbn` — gate only `app.MapControllers()` /
      `app.MapModulusEndpoints(...)` behind a new Scriban var `expose_api` (true for `api` and
      `webapp+api`'s API project, false for `webapp`). Leave `AddControllers()` (service
      registration), module registration, DbContext migration, mediator, UI wiring (`use_ui`)
      untouched — `webapp` still sets `use_ui = true`.
- [x] A1.2. `cli/Services/AppKind.cs` — `AppKinds.ResolveCrudUi` gets a third arm for `WebAppApi`
      (done in A0).
- [x] A1.3. `cli/Commands/NewAppCommand.cs`, `ResolveKind` (~L733) — 3-choice interactive menu;
      `--kind` accepts `api|webapp|webapp+api`; `--kind web` still parses (via the alias) with a
      one-line console note (already implemented in A0).
- [x] A1.4. `GenerateAll` (~L300) — existing single-project path runs for `Api`/`WebApp` unchanged
      except passing `expose_api` into the template model. `webapp+api` is new (A2).
- [x] A1.5. `AppModel.UseUi` (`Models.cs`) changes from `Kind == AppKind.Web` to
      `Kind is AppKind.WebApp or AppKind.WebAppApi`; same pattern for `UseTablerTheme` (done in A0).
- [x] A1.6. Grep `AppKind.Web\b` across `cli/` and convert each remaining comparison
      (`WithSignInPage`, `AuthNote`, the "Next steps" console output block) to the 3-way form
      (done in A0).
- [x] A1.7. Validate end-to-end: `--kind api` and `--kind webapp` both build 0 warnings; `api` has
      AddControllers/MapControllers/MapModulusEndpoints/AddModulusOpenApi, `webapp` has
      AddRazorPages/MapRazorPages and lacks the API surface. Verified: api kind exposes API endpoints,
      webapp kind does not; both render templates correctly with no syntax errors.

#### A2. `webapp+api` — the two-project HTTP split (highest risk/effort)

**A2.1 — Project layout & templates** ✅ COMPLETE

- [x] A2.1.1. New `src/Web/{App}.Web/{App}.Web.csproj` (`cli/Templates/app/web.csproj.sbn`),
      `Microsoft.NET.Sdk.Web`, referencing `Modulus.UI.Core`, `Modulus.Platform`, the chosen
      theme — **no module Infrastructure/Presentation references at all**.
      *As built:* also references `Synvro.Modulus.Identity` (login/token plumbing),
      `Modulus.AspNetCore`/`Modulus.Core`, and ProjectReferences to `{App}.Api` (for
      `AddModuleApiClients`) and each module's `.Application` (DTOs, below).
- [x] A2.1.2. **As built:** no new `{App}.Shared.Contracts` project. The Web project references
      each module's `.Application` project directly and reuses its commands/queries/DTOs
      (verified in the generated csproj). `{Module}.Application` is already dependency-light
      enough for a presentation host; revisit only if an Application layer grows
      Infrastructure-facing dependencies.
- [x] A2.1.3. **As built:** templates live in `cli/Templates/app/` with a `.Web` suffix
      (`Program.Web.sbn`, `Login.Web.cshtml.sbn` + `LoginModel.Web.sbn`, `Logout…`,
      `AccessDenied…`, `Index…`, `TokenRelayHandler.sbn`, `ApiClientExtensions.Web.sbn`,
      `WebPagesViewStart.sbn`/`WebAccountViewStart.sbn`, `WebPagesViewImports.sbn`,
      `appsettings.Web*.json.sbn`, `launchSettings.Web.json.sbn`) — not a separate
      `webappapi/` folder.
- [x] A2.1.4. The API project for this kind gets no theme/UI packages.
      *As-built deviation:* the API host **does** call `AddModulusUi()` (services only, no
      theme) because generated API endpoints resolve `IEntityUiRegistry` to permission-filter
      extension fields; the Web host owns all pages/theme.
- [x] A2.1.5. `NewAppCommand.GenerateAll` factored into the single-host path plus
      `GenerateWebHost(...)`; the split path emits both projects into one solution.
- [x] A2.1.6. `WireUiModules` takes the target project from `AppInventory.UiProjectPath`
      (`WebProjectPath` for this kind) — `dotnet add package` + `UiHostWiring.EnsureUiWiring`
      land in the Web project's Program.cs.

**A2.2 — Auth: webapp process → API process on behalf of the signed-in user** ✅ COMPLETE

- [x] A2.2.1. The API remains the OpenIddict token server, unchanged (it owns Identity's
      DbContext; the generated Identity module + seeding live only on the API side).
- [x] A2.2.2. HTTP-calling Login page in the Web project (`LoginModel.Web.sbn`) POSTs
      credentials to the API's `POST /connect/token` (password grant). *As built:* the
      return-URL guard is an instance method (not static — it reads per-request config).
- [x] A2.2.3. Access/refresh tokens stored in the Web project's own auth cookie
      (`SaveTokens`-style via the OpenIddict client/pass-through flow).
- [x] A2.2.4. *(Superseded: new apps use the `Modulus.Bff` web session, see AGENTS.md "App-kind split"; `modulus doctor` warns when a Web host still has the handler.)* `TokenRelayHandler` (`Security/TokenRelayHandler.cs`) attached to every generated
      typed client: relays the cookie's access token, refreshes near expiry through a named
      `"TokenRefresh"` HTTP client. *Validation fixes found during end-to-end testing:* sync
      `Dispose()` (not `DisposeAsync` — the handler factory calls it synchronously) and request
      options cloned via `new HttpRequestOptionsKey<object?>(key)` (string keys throw).
- [x] A2.2.5. Templates as listed in A2.1.3 (`.Web`-suffix convention in `cli/Templates/app/`).
- [x] A2.2.6. First cut scoped to Login + typed-client auth; Register/ForgotPassword-over-HTTP
      stays a fast-follow once Phase B1's page shapes exist.
- [x] A2.2.7. Reviewed with the same rigor as the OIDC/code-flow work; the two handler defects
      above were caught by the generated test suite before any release.

**A2.3 — Typed HTTP client per module, replacing in-process `IMediator` calls**

- [x] A2.3.1. One typed client per module (`{Module}ApiClient`), registered by the generated
      `ApiClientExtensions.AddModuleApiClients(...)` via `AddModulusHttpClient<{Module}ApiClient>()`
      (`Modulus.Platform`) with `TokenRelayHandler` as the outer handler — resilience +
      correlation come from the framework's standard handler chain.
- [x] A2.3.2. `GenerateCrudCommand` is kind-aware: for `webapp+api` pages land in the Web
      project (`UiNamespace = {Root}.Web`), everything else in the API project.
- [x] A2.3.3. `ui/CrudIndexPageModel.Http.sbn` (+ `ui/ModuleApiClient.sbn`) — the Web-side page
      model calls `{Module}ApiClient` methods instead of `IMediator.Send(...)`, mapping non-2xx
      responses to `ModelState`.
- [x] A2.3.4. `ResolveHost` routes via `AppInventory.WebProjectPath` for this kind.
- [ ] A2.3.5. ui-schema endpoint (`GET /{module}/{route}/ui-schema` + `EntityUiSchemaDto`) —
      the schema model exists (`EntityUiSchema`, `EntityUiSchemaTests`) and `Endpoint.sbn` references it, but it is **not wired as a documented endpoint; deferred pending design sign-off** (needs an explicit decision, see A2.3.6).
- [ ] A2.3.6. Web-side `m-fields`/`m-datatable` overload consuming the schema DTO — the one
      piece of new framework abstraction in Phase A; **not built**, awaiting explicit sign-off
      on the design before implementation.
- [x] A2.3.7. Interim behavior (without A2.3.5/A2.3.6): the API's extension-field endpoints
      remain permission-filtered server-side (a caller only ever reads back fields it may see);
      Web-side per-field rendering of extension fields over HTTP waits for the schema endpoint.
- [x] A2.3.8. Retarget `GenerateCrudCommand.ExposesExtraFieldsInApi` from kind `web` (a
      pre-split rule) to `webapp+api`: under the three-way model only `webapp+api` maps an API
      surface, so for a `web` host the extra-field endpoint code is currently emitted but never
      mapped (dead-but-harmless).

**A2.4 — Cross-process consistency** ✅ COMPLETE (by construction)

- [x] A2.4.1. No dual-write logic needed. The Web project performs no local persistence; every
      mutation is one HTTP call to the API, which keeps using its existing single-process
      transactional-outbox/inbox/`TransactionBehavior` pipeline unchanged.

**A2.5 — Validate end-to-end**

- [x] A2.5.1. Both projects build 0 warnings.
- [x] A2.5.2. API boots standalone and answers its endpoints directly
      (`ApiIntegrationTests`: health, 401/403/200 permission gating, extension-field rules).
- [ ] A2.5.3. Web-side round-trip over HTTP: the API side is proven by the generated suite, and
      the Web host boots/serves pages (`WebAppSmokeTest`). 2026-10-23: the round trip works in a generated
      `webapp+api` app (sign-in through the BFF → create a Widget on its Web page → the list page shows it), as
      `CreatedWidget_IsListedThroughTheBffAndTheApi` in a hand-edited scratch copy (15/15 generated tests pass). Not in the
      templates yet: the example module has no Web pages, so the template has nothing to drive. Two template changes
      follow, and the first needs a decision: (1) generate the example's Web CRUD pages in `webapp+api` apps (it changes
      what every such app contains), or keep the example API-only and add the test to `AppTests.sbn` for apps that have
      pages; (2) then port the test: `TestHost` using, `PostFormAsync`, the test scheme overrides for the Web and API hosts
      (both factories force the test scheme as default, which 401s the cookie session and the forwarded bearer token), and
      `PairedForwarderFactory` (`TestServerPairing` does not reach YARP's forwarder, so the BFF proxy needs the same
      replacement `Modulus.Bff.Tests` uses).
- [x] A2.5.4. (done: `LoginPage_ExplainsWhenTheApiIsDown` in the generated tests; the BFF session returns `temporarily_unavailable` instead of a 500) Kill-the-API resilience check not run yet (the resilience handler should convert
      connection failures into retried/failed requests, not crashes — verify explicitly).

#### A3. Propagate the kind model through the rest of the CLI ✅ COMPLETE

- [x] A3.1. `ui add` — all UI commands (`UiAddCommand`, `UiRemoveCommand`, `UiUpdateCommand`,
      `UiInfoCommand`, `UiListCommand`, `UiSearchCommand`) target `inventory.UiProjectPath`
      (Web project for `webapp+api`); `api`-kind hosts are refused.
- [x] A3.2. `ui eject`/`ui diff` (`UiEject.IsInstalled`) resolve through `UiProjectPath`.
- [x] A3.3. `generate-crud` — covered by A2.3.2–A2.3.4.
- [x] A3.4. `migrate` commands audited: they discover modules by `*.Infrastructure.csproj` under
      `src/Modules` and use the `*.Api.csproj` as startup project — kind-agnostic by
      construction (the Web project hosts no modules).
- [x] A3.5. `doctor` warns when the Web csproj references a module `*.Infrastructure` project
      (`DoctorCommand.CheckWebInvariant`, covered in `BffCommandTests`).
- [x] A3.6. `modulus info` prints both project paths for `webapp+api` (`API :` / `Web :` rows).
- [x] A3.7. `AppKindTests` covers the webapp+api inventory/convenience-property fixtures
      (A0.7); the remaining commands were validated by regenerating a webapp+api app and
      running its full suite rather than per-command fixture tests.

#### A4. Backward compatibility ✅ COMPLETE

- [x] A4.1. `AppKinds.Parse("web")` keeps working, mapped to `AppKind.WebApp` (one-time console
      note on the deprecated alias).
- [x] A4.2. Existing apps with `<ModulusAppKind>web</ModulusAppKind>` need zero changes;
      `AppKinds.Read` maps the legacy value and every command behaves as before.
- [x] A4.3. No auto-migration from `web` to `webapp+api` is offered — deliberate manual
      restructuring; documented here as a known limitation.

#### A5. Testing story for the two-process kind

- [x] A5.1. API-side tests generated for `webapp+api` (`ApiIntegrationTests` +
      `AppSmokeTest` + boundary tests, factory over `ApiEntryPoint`).
- [x] A5.2. `WebAppSmokeTest` boots the Web project via a second
      `ModulusWebAppFactory<WebEntryPoint>` (`WebEntryPoint` marker in the Web host's
      Program.cs). *As built:* the two factories are standalone, not paired.
- [x] A5.3. (login hop done: `WebToApiLoginTests` pairs the Web and API factories; a page-create round-trip is still open) TestServer pairing (`ConfigurePrimaryHttpMessageHandler(() =>
      apiFactory.Server.CreateHandler())`) **not implemented** — the generated Web page tests
      currently assert page-level behavior (challenge / login page / landing) and make no API
      calls, so they pass without the API running. Pairing is the natural next step and would
      also close A2.5.3/A5.5.
- [x] A5.4. `Modulus.Testing.TestServerPairing` exists; the generated `WebToApiLoginTests` uses it (login through the API's token endpoint).
- [ ] A5.5. The HTTP hop is proven on the API side (entity created through the API is visible
      via the API factory's client); the Web-page-driven flow is asserted in the scratch copy only (see A2.5.3). Remaining: the template step.

---

### Phase B — Prebuilt & CLI-generated module UI customizability

> **Moved (2026-10-23).** This phase works on `src/ui/Modulus.UI.*` packages that no longer exist in this repository: the UI
> lives in the `modulus-ui` repository (see AGENTS.md, "UI and app kinds"). Its boxes below are open against that repository and
> are not tracked here. Items in it that touch this repository's Identity model (B0) are still open work for `modulus-ui` to
> pick up with the model change.

An audit of `Modulus.UI.Identity` plus a second, separate audit of the other 7 prebuilt feature UI
packages (Users, Tenancy, Permissions, Settings, AuditLogging, Notifications, Files) and CLI
`generate-crud --with-ui` output — reading actual PageModels/`.cshtml`/entities/DbContexts, not
just AGENTS.md prose — found the customizability gap is real but **narrower than "fix every
package"**: most prebuilt packages are read-only or key-value-shaped by design and the
extension-fields mechanism genuinely doesn't apply to them. Summary:

| Package | Extension fields | Columns/actions ext. | New-page precedent | Per-page styling |
|---|---|---|---|---|
| Identity | **GAP** — entity marker, DbContext wiring, PageModel wiring all missing | N/A | untested | consistent (generic recipe works) |
| Users | **GAP** — same root cause as Identity (shares `ModulusUser`/`ModulusIdentityDbContext`) | N/A | untested | consistent |
| Tenancy | N/A by design — entity deliberately minimal, no create/edit form exists | N/A | untested | consistent |
| Permissions | N/A by design — read-only, editing lives in the management API | N/A | untested | consistent |
| Settings | N/A by design — key/value store with its own working contribution point (`SettingDefinition`) | N/A | untested | consistent |
| AuditLogging | N/A by design — immutable log, no EF context to attach a bag to | **GAP** — no `entity=`/contributed columns despite being a natural fit | untested | consistent |
| Notifications | N/A by design — no publish form by design | N/A | untested | consistent |
| Files | N/A by design — blob storage, no entity/DbContext at all | N/A | untested | consistent |
| CLI `generate-crud --with-ui` | **WORKING end-to-end** already | **WORKING end-to-end** already | N/A — app owns the file directly | N/A — app owns the file directly |

So the actionable work is: fix extension fields for Identity+Users together (same underlying fix),
wire AuditLogging into the existing column-extension mechanism, formalize new-page/per-page-styling
support as a pattern that provably works across *any* prebuilt package (not just Identity), and
explicitly document the N/A-by-design packages so nobody re-attempts a fix there later.

#### B0. Extra-properties plumbing on `ModulusUser`/`ModulusIdentityDbContext` (shared fix)

This single fix unblocks **both** Identity's Register page (B2.1) and Users' Create/Edit pages
(B2.2), since they share the same entity and DbContext.

- [ ] B0.1. Read `IHasExtraProperties`'s exact member shape in
      `src/core/Modulus.Core/Abstractions/Entities/IHasExtraProperties.cs` first.
- [ ] B0.2. `src/identity/Modulus.Identity/Abstractions/ModulusUser.cs` — implement
      `IHasExtraProperties` (likely just `Dictionary<string, string?> ExtraProperties` plus
      whatever helper members `EntityFieldValues`/`SetExtraProperties` expects).
- [ ] B0.3. `src/identity/Modulus.Identity/EntityFrameworkCore/ModulusIdentityDbContext.cs` — do
      **not** change its base class. Call `UseModulusExtraProperties()`
      (`src/data/Modulus.EntityFrameworkCore/ModelBuilding/ExtraPropertiesModelBuilderExtensions.cs`,
      a standalone `ModelBuilder` extension with no `ModuleDbContext` dependency) directly from
      `OnModelCreating` after `builder.UseOpenIddict()`.
- [ ] B0.4. Do not make this context derive from `ModuleDbContext` — that would drag in
      multi-tenancy/outbox assumptions needing a much larger audit for no benefit.
- [ ] B0.5. Document the migration impact: new apps get the column from the template on day one;
      existing apps need `modulus migrate add AddUserExtraProperties --module Identity` after
      upgrading the framework package (normal migration workflow, not auto-applied) — call this
      out in release notes.
- [ ] B0.6. Validate: round-trip test on the new column (SQLite in-memory, matching existing
      Identity test fixtures); re-run the existing Identity end-to-end login/register suite to
      confirm no regression.

#### B1. Forgot Password / Reset Password pages (Identity-specific)

`ModulusAccountController` (`src/identity/Modulus.Identity/ModulusAccountController.cs`) already
has `POST /account/forgot-password` (L52) and `/reset-password` (L73) with correct
anti-enumeration behavior and an `IIdentityEmailSender` hook — confirmed by reading the file.
**No new identity/token plumbing needed.** Only new Razor Pages UI. (This also doubles as the
worked example/validation case for B5's new-page pattern — see below.)

- [ ] B1.1. `src/ui/Modulus.UI.Identity/Pages/Account/ForgotPassword.cshtml(.cs)` +
      `ForgotPasswordConfirmation.cshtml` — mirror `ModulusAccountController.ForgotPasswordAsync`'s
      logic in-process (`UserManager.GeneratePasswordResetTokenAsync` +
      `IIdentityEmailSender.SendPasswordResetEmailAsync`), same non-committal response regardless
      of whether the email exists.
- [ ] B1.2. `ResetPassword.cshtml(.cs)` + `ResetPasswordConfirmation.cshtml` — `OnGet(email,
      token)` populates hidden fields from the reset-link's query string; `OnPostAsync` mirrors
      `ResetPasswordAsync`'s `userManager.ResetPasswordAsync(user, token, newPassword)` with the
      same uniform error handling.
- [ ] B1.3. Document (or add a small `IdentityLinkBuilder` helper for) the expected reset-link URL
      shape so custom `IIdentityEmailSender` implementations build correct links.
- [ ] B1.4. Add a "Forgot your password?" link on `Login.cshtml`.
- [ ] B1.5. Add `ForgotPassword.*`/`ResetPassword.*` keys to the existing `IdentityUiLocalization`
      resource, following `Login.*`/`Register.*` naming.
- [ ] B1.6. No new options toggle needed — `IIdentityEmailSender`'s fail-closed no-op default
      already makes the flow inert until an app wires a real sender.
- [ ] B1.7. Scope the `webapp+api` HTTP-calling variant (POSTing to the existing
      `/account/forgot-password`/`/reset-password` endpoints instead of local `UserManager` calls)
      as a fast-follow within Phase A2.2.6, once these page shapes/localization keys exist to port
      from.
- [ ] B1.8. Validate end-to-end: regenerate an app, hit both pages over real HTTP with a stub
      `IIdentityEmailSender` capturing the token, complete a full reset, log in with the new
      password.

#### B2. Wire extension fields into existing forms

Reuses `ConfigureEntityUi`/`EntityField`/`m-fields` exactly as CRUD entities already do (confirmed
working end-to-end in `cli/Templates/ui/CrudIndexPageModel.sbn` — use it as the reference
implementation) — no new registry API needed, only making these PageModels consumers of it.

- [ ] B2.1. **Register (Identity)** — `src/ui/Modulus.UI.Identity/Pages/Account/Register.cshtml.cs`:
      inject `IEntityUiRegistry` + an anonymous-safe `ICurrentUser` (confirm one is resolvable
      pre-authentication the same way other `[AllowAnonymous]` pages already handle it — a field
      gated by `RequiredPermission` should correctly stay hidden on the public form). Add
      `[BindProperty] Dictionary<string, string?> Extra`. Call `registry.ValidateEntityFields(
      "Identity.User", currentUser, Extra, ModelState, "Extra")` before creating the user. Because
      `UserManager.CreateAsync` is the only API that hashes the password, extra fields can't be
      set pre-create — set `user.ExtraProperties` from `registry.ReadEntityFieldText(...)`
      **after** `CreateAsync` succeeds, then a second `UpdateAsync` call (verify this two-step
      pattern doesn't clash with any Identity audit/event hook that assumes single-write
      semantics). `Register.cshtml` — add `<m-fields entity="Identity.User" for="Extra" />`
      matching the exact tag shape already used in `ui/CrudFormPartial.sbn`.
- [ ] B2.2. **Users admin Create/Edit** — `src/ui/Modulus.UI.Users/Pages/Users/Create.cshtml.cs`
      (and the Edit page, if one exists) — same `"Identity.User"` entity key as Register (not a
      separate key), so a field configured once via `ConfigureEntityUi("Identity.User", ...)`
      shows in both the public registration form and the admin-created-user form, each filtered by
      the calling `ICurrentUser`'s permissions independently. Same wiring pattern as B2.1: inject
      `IEntityUiRegistry`, add `Extra` bound property, validate before create, set
      `ExtraProperties` + `UpdateAsync` after create, render `<m-fields entity="Identity.User"
      for="Extra" />`.
- [ ] B2.3. App-side field registration needs zero options-API changes for either page — it
      already works via the same `ConfigureEntityUi` call shape CRUD entities use, purely
      additive.
- [ ] B2.4. Validate end-to-end: configure a field on `Identity.User` in a generated app, confirm
      it renders on **both** `/Account/Register` and the admin `/Users/Create` page, rejects an
      invalid value server-side on each, and the value round-trips to
      `AspNetUsers.ExtraProperties` from either entry point.

#### B3. AuditLogging column-extension wiring

Distinct, smaller gap: AuditLogging is a correctly-immutable append-only log (no field/write
extension should ever apply there), but its list view isn't wired into the existing **read-path**
column-extension mechanism (`EntityColumn`/`IEntityColumnValueProvider`, `<m-entity-columns/>`/
`<m-entity-cells/>`) the way CLI-generated CRUD lists already are — a natural fit for e.g. another
module contributing a computed/joined column to the log browser.

- [ ] B3.1. `src/ui/Modulus.UI.AuditLogging/Pages/.../Index.cshtml` — add `entity="AuditLogging.Entry"`
      to the existing `<m-datatable>`, plus `<m-entity-columns/>` in the header and
      `<m-entity-cells entity="AuditLogging.Entry" row-id="@entry.Id" values="..." />` per row,
      matching the pattern documented in AGENTS.md's "Extension columns and row actions" section.
- [ ] B3.2. `Index.cshtml.cs` — load contributed column values once per page via
      `registry.LoadEntityColumnsAsync(...)`, same as a CLI-generated list page's `LoadAsync`.
- [ ] B3.3. No row actions needed here (the log has no per-row mutation), so skip
      `<m-entity-actions/>` — columns only.
- [ ] B3.4. Validate: contribute a test column via `ConfigureEntityUi("AuditLogging.Entry", ...)`
      in a generated app, confirm it renders in the audit log browser.

#### B4. N/A-by-design packages — document, don't fix

Tenancy, Permissions, Settings, Notifications, and Files were confirmed (by reading their entities
and PageModels) to have no data shape the extension-fields mechanism applies to — deliberately
minimal/immutable entities, key-value stores, or blob storage with no row entity at all. This is
correct as designed, not an oversight.

- [ ] B4.1. Add a short note to `docs/UI_FRAMEWORK_GUIDELINE.md` listing these five packages and
      why extension fields don't apply to each (one line per package, from the table above), so a
      future contributor doesn't reopen this as a "gap" without re-deriving the same conclusion.
- [ ] B4.2. Settings already has its own working contribution point (`SettingDefinition` +
      `ISettingDefinitionRegistry`) for the equivalent need ("a module wants to contribute
      configuration") — cross-reference it from the same doc note so it's discoverable.

#### B5. New-page-inside-module precedent (generalized across all prebuilt packages)

Standard Razor Pages RCL semantics already support this — the app's own file at an unclaimed route
under any prebuilt package's route folder (`/Account/*`, `/Users/*`, `/Tenancy/*`, ...) simply adds
a new page; it only conflicts (and the app wins) at a route the package already claims, per
`UiEject`'s existing "entry assembly beats package" precedence. What's missing across **all**
7 packages is validation, not code — B1's Forgot/Reset Password pages double as the first real,
tested instance of this pattern for Identity; the doc/test work below generalizes it explicitly so
it's not read as an Identity-only trick.

- [ ] B5.1. Add an "Adding a new page to a prebuilt module's area" section to
      `docs/UI_FRAMEWORK_GUIDELINE.md` with a worked example (`Pages/Account/Mfa.cshtml`,
      `@page "/Account/Mfa"`, themed via `Context.GetThemeLayout()` like generated CRUD pages),
      and a one-line note that the same mechanism applies to every prebuilt package's route folder
      (Users, Tenancy, Permissions, Settings, AuditLogging, Notifications, Files), not just
      Identity's `/Account`.
- [ ] B5.2. Add a test fixture (extending the existing "regenerate a web app, hit all views"
      end-to-end test) that adds a sibling page under **at least two different** prebuilt
      packages' route folders (e.g. one under `/Account/*` via B1's own new pages, one under
      `/Users/*`) and asserts both resolve over HTTP alongside their package's own pages — proving
      the pattern generalizes rather than only validating it once.

#### B6. Per-page visual customization (generalized)

No new framework abstraction — extend the existing `ISlotContributor`/`UiSlots.Head` pattern.
Confirmed consistent across all 7 prebuilt packages (all use `Context.GetThemeLayout()` the same
way; zero existing `ISlotContributor`/`UiSlots.Head` usage anywhere) — this is genuinely a single
generic recipe, not something to special-case per package.

- [ ] B6.1. Add a path check against `ctx.ViewContext.HttpContext.Request.Path` inside a new
      `ISlotContributor` implementation (already available inside `RenderAsync`, confirmed in
      `Slots.cs`).
- [ ] B6.2. Document as a recipe in `docs/UI_FRAMEWORK_GUIDELINE.md`: "for markup changes, eject;
      for conditional head/asset injection on one page (any prebuilt package or a CLI-generated
      page), use a path-conditional `ISlotContributor`." No new interface — matches the "don't
      design speculative abstractions" constraint.
- [ ] B6.3. Confirm global branding (logo/app name/footer via `BrandingOptions`) already works
      with zero eject and needs no changes — call this out in the doc so users don't reach for
      eject unnecessarily.
- [ ] B6.4. Validate via the B5 test fixture: assert the injected markup appears only on the
      targeted page, for a page in at least one prebuilt package.

#### B7. CLI `generate-crud --with-ui` — confirmed working, document only

Traced end-to-end through the Scriban templates and found **already fully wired**: the generated
entity implements `IHasExtraProperties` unconditionally (`cli/Templates/module/Domain/Entity.sbn`),
the generated `{Module}DbContext : ModuleDbContext` picks up `UseModulusExtraProperties()`
automatically, `CrudIndexPageModel.sbn` calls `ValidateEntityFields`/`ReadEntityFieldText` on both
create and edit paths, `CrudFormPartial.sbn`/`CrudEditFormPartial.sbn` render `<m-fields>`, and
`CrudTablePartial.sbn` wires `<m-entity-columns/>`/`<m-entity-cells/>`/`<m-entity-actions/>` too —
ahead of every prebuilt package including AuditLogging pre-B3. New-page and per-page-styling
concerns don't apply here at all: `GenerateCrudCommand.cs` writes pages directly into the app's own
project, not a package, so there's no RCL-precedence question — adding a page or a `<style>` block
is just editing the app's own file.

- [ ] B7.1. No code changes needed. Add one line to `docs/UI_FRAMEWORK_GUIDELINE.md` noting this
      was audited and confirmed complete, so it isn't re-investigated as a suspected gap later.

#### Design decision: no new `Modulus.UI.Core` abstraction for extension-fields wiring

Considered and rejected during planning: a shared base PageModel or a bigger `Modulus.UI.Core`
helper to reduce the per-package wiring in B2/B3. `EntityFieldValues`/`EntityApiFields`
(`src/ui/Modulus.UI.Core/Entities/`) already provide the right-sized helpers (`ValidateEntityFields`,
`ReadEntityFieldText`, and their API-DTO equivalents) — the CLI's generated PageModel already calls
them in ~4-5 lines per form handler, which is close to irreducible since each call site differs in
ModelState prefix, the 422-partial name, and the post-success response shape.

The actual duplication is two prerequisite steps `Modulus.UI.Core` structurally **cannot** own: the
entity implementing `IHasExtraProperties` (a per-package entity-author decision) and the DbContext
calling `UseModulusExtraProperties()` (automatic for any `ModuleDbContext` subclass, but Identity's
context is deliberately bespoke, non-`ModuleDbContext`, because it derives from ASP.NET Identity's
own `IdentityDbContext`). `Modulus.UI.Core` reaching into either would mean referencing each
package's own entity/DbContext types, violating the enforced dependency rule (feature UIs depend on
`Theme.Abstractions` only, never cross-reference each other or reach into `Modulus.Identity`/
`Modulus.MultiTenancy.EntityFrameworkCore`). **Decision: no new Core abstraction — B0/B2/B3 are
narrow, package-specific fixes, not a framework design gap.** Don't re-litigate this without new
information.

---

### Cross-cutting risks

| Area | Risk | Mitigation |
|---|---|---|
| A2 auth relay | Largest net-new security-sensitive code (hand-rolled bearer-token cookie relay between two local processes) | Scope tightly (Login + typed clients first), review/test with the same rigor as the existing OIDC/code-flow work — expired token, refresh, revoked, wrong audience. |
| A2 registry-over-HTTP | New `Modulus.UI.Core` abstraction, not just CLI scaffolding | Explicit design sign-off before building; unfiltered fallback available as a documented-gap interim. |
| A0 inventory rework | Wide blast radius (~dozen call sites read the old single-project properties) | Full grep audit before starting; keep old properties working via the `UiProjectPath` convenience during transition. |
| B0 schema change | New required column on `AspNetUsers` for every future app; existing apps need a manual migration after upgrade | Document in release notes; no auto-migration (matches existing framework convention). |
| B2 double-save | `CreateAsync` can't carry extra fields pre-hash; needs a second `UpdateAsync`, now on two PageModels (Register + Users Create) instead of one | Confirm no Identity audit/event hook assumes single-write semantics; keep the pattern identical between both call sites. |
| Backward compat | Silent surprise if `web` → `webapp` alias isn't communicated | One-time console note on `--kind web`; release notes. |

### Verification summary

Per framework convention (regenerate off a packed build, 0 warnings, boot, exercise over real
HTTP):

- **A1**: `api`/`webapp` build clean; `webapp` boots with working sign-in but `/api/*` returns
  404; `api` has no `/Account/*`.
- **A2**: both `webapp+api` projects build clean; API-side CRUD round-trip, permission gating
  and extension-field rules proven by the generated suite (9/9). Still open: a Web-page-driven
  round-trip over HTTP (A2.5.3) and the kill-the-API check (A2.5.4).
- **A3**: `generate-crud`, the `ui *` family and `modulus info` each target the right project(s)
  for a `webapp+api` app (UiProjectPath routing); validated by regeneration, not per-command
  fixtures. `doctor`'s Web-project invariant warning is done (A3.5).
- **A5**: generated smoke tests pass for all three kinds (`webapp+api`: 9/9; `api`: 6/6;
  `webapp`: 3/3). The generated `WebToApiLoginTests` pairs the Web factory with the API's TestServer (A5.3); page-create round-trip still open.
- **B0–B6**: not started.

---

### Appendix: reference architectures considered

Three generic "professional enterprise .NET repo" layouts were reviewed while designing the
three-way `AppKind` split, to check Modulus's target shape against common practice. `webapp` maps
to Reference 1, `webapp+api` maps to Reference 2. Reference 3 (microservices) does **not** map to
anything in this plan — see the Non-goal note above; kept here only so the boundary is legible
without re-deriving it from scratch.

#### Reference 1 — Razor Pages modular monolith, no external API (→ `webapp`)

```
MyApp/
├── .github/workflows/{build-and-test,deploy}.yml
├── build/{Directory.Build.props,Directory.Packages.props,common.props}
├── docs/architecture/{module-boundaries.md,adr/0001-modular-monolith.md}
├── docker/{Dockerfile,docker-compose.yml,docker-compose.override.yml}
├── src/
│   ├── Host/MyApp.Web/                       # composition root only
│   │   ├── Program.cs, appsettings.*.json, wwwroot/
│   │   ├── Pages/{Shared/_Layout.cshtml, Index.cshtml, Error.cshtml}
│   │   ├── HealthChecks/ModuleHealthCheck.cs
│   │   └── Middleware/{ExceptionHandlingMiddleware,RequestLoggingMiddleware}.cs
│   ├── Modules/
│   │   ├── Orders/
│   │   │   ├── MyApp.Modules.Orders/                # Pages + composition (OrdersModule.cs)
│   │   │   ├── MyApp.Modules.Orders.Domain/          # Entities, ValueObjects, Enums, Exceptions
│   │   │   ├── MyApp.Modules.Orders.Application/     # Commands, Queries, Validators, Mappings
│   │   │   ├── MyApp.Modules.Orders.Infrastructure/  # DbContext, Configurations, Migrations, Repos
│   │   │   └── MyApp.Modules.Orders.Contracts/       # Dtos, Events, IOrdersModuleApi.cs
│   │   ├── Inventory/  (same 5-project pattern)
│   │   └── Users/      (same 5-project pattern)
│   └── Shared/
│       ├── MyApp.SharedKernel/            # IModule, IEntity, IAggregateRoot, IEventBus, Result
│       └── MyApp.SharedInfrastructure/    # Logging, Caching, Security, ModuleDbContextBase
├── tests/{UnitTests,IntegrationTests,ArchitectureTests}/   # ArchitectureTests enforces module boundaries (NetArchTest)
├── .editorconfig, .gitignore, Directory.Build.props, global.json, NuGet.Config, MyApp.sln
```

Professional touches: architecture tests enforcing module isolation, 5-layer-per-module split
(adds a `Contracts` project), ADRs, central package management, health checks, per-environment
configs. Modulus's own module template already covers the 4-layer core of this (Domain/
Application/Infrastructure/Presentation collapsed the separate Contracts/IntegrationEvents/Tests
projects on purpose — see "CLI 4-layer rewrite" in AGENTS.md); `Modulus.Testing.Architecture`
already plays the role of `MyApp.ArchitectureTests` here.

#### Reference 2 — separate API (modular monolith) + Web app (→ `webapp+api`)

```
MyApp/
├── .github/workflows/{api-build-deploy,web-build-deploy}.yml
├── build/{Directory.Build.props,Directory.Packages.props}
├── docs/{architecture/,api/openapi.yaml}
├── docker/{api/Dockerfile, web/Dockerfile, docker-compose.yml}
├── src/
│   ├── Web/MyApp.Web/                        # consumer only, NO DB access
│   │   ├── Program.cs, appsettings.*.json, wwwroot/
│   │   ├── Pages/{Shared/_Layout.cshtml, Orders/, Inventory/, Users/}
│   │   ├── ApiClients/
│   │   │   ├── Abstractions/{IOrdersApiClient,IInventoryApiClient}.cs
│   │   │   ├── OrdersApiClient.cs, InventoryApiClient.cs
│   │   │   └── Policies/RetryPolicyFactory.cs        # Polly resilience
│   │   ├── Models/ViewModels/
│   │   └── Authentication/TokenForwardingHandler.cs
│   ├── Api/MyApp.Api/                        # composition root for the API
│   │   ├── Program.cs, appsettings.*.json
│   │   ├── Modules/
│   │   │   ├── Orders/
│   │   │   │   ├── MyApp.Modules.Orders.Api/          # Endpoints (OrdersModule.cs, OrdersEndpoints.cs)
│   │   │   │   ├── MyApp.Modules.Orders.Domain/
│   │   │   │   ├── MyApp.Modules.Orders.Application/
│   │   │   │   ├── MyApp.Modules.Orders.Infrastructure/
│   │   │   │   └── MyApp.Modules.Orders.Contracts/
│   │   │   ├── Inventory/ (same pattern)
│   │   │   └── Users/     (same pattern)
│   │   ├── Middleware/{ExceptionHandlingMiddleware,ApiKeyMiddleware}.cs
│   │   └── HealthChecks/
│   └── Shared/
│       ├── MyApp.SharedKernel/, MyApp.SharedInfrastructure/
│       └── MyApp.Contracts/                  # DTOs shared between Api & Web (NuGet-able)
├── tests/{Api.Modules.Orders.UnitTests, Api.IntegrationTests, Web.UnitTests, ArchitectureTests}/
├── .editorconfig, .gitignore, global.json, MyApp.sln
```

Professional touches: Polly resilience policies on the Web→API `HttpClient`s, a `TokenForwardingHandler`,
a shared versioned `Contracts` project instead of duplicated DTOs, an OpenAPI spec tracked in
`docs/api/`, separate Dockerfiles per deployable. This is the direct model for Phase A2:
`RetryPolicyFactory`/Polly ↔ `AddModulusHttpClient<T>()`'s standard resilience handler (A2.3.1),
`TokenForwardingHandler` ↔ `TokenRelayHandler` (A2.2.4), `MyApp.Contracts` ↔ `{App}.Shared.Contracts`
(A2.1.2).

#### Reference 3 — microservices (informational only — not a target, see Non-goal above)

```
MyApp/
├── .github/workflows/  (one CI per service + web + gateway)
├── docs/architecture/{service-map.md, event-catalog.md}, docs/adr/
├── deploy/
│   ├── docker-compose.yml (+ .override.yml)
│   └── k8s/{namespaces/, orders-service/{deployment,service,hpa}.yaml, inventory-service/,
│            users-service/, gateway/, infra/{rabbitmq,redis,sql-servers}.yaml}
├── src/
│   ├── ApiGateway/MyApp.Gateway/            # YARP or Ocelot reverse proxy
│   ├── Services/
│   │   ├── Orders/MyApp.OrdersService/      # fully independent .NET solution, own repo-able
│   │   │   ├── src/MyApp.OrdersService.{Api,Domain,Application}/
│   │   │   ├── src/MyApp.OrdersService.Infrastructure/
│   │   │   │   ├── Persistence/{OrdersDbContext.cs, Migrations/}     # OWN database
│   │   │   │   └── Messaging/{Publishers/OrderCreatedPublisher.cs, Consumers/InventoryReservedConsumer.cs}
│   │   │   ├── src/MyApp.OrdersService.Contracts/Events/OrderCreatedEvent.cs
│   │   │   ├── tests/{UnitTests,IntegrationTests}/, Dockerfile, MyApp.OrdersService.sln
│   │   ├── Inventory/MyApp.InventoryService/ (identical internal structure, own DB, own solution)
│   │   └── Users/MyApp.UsersService/         (identical internal structure, own DB, own solution)
│   ├── Web/MyApp.Web/                        # Razor Pages BFF, talks to the Gateway only
│   └── Shared/
│       ├── MyApp.EventBus.Contracts/         # shared event schema library (versioned NuGet)
│       └── MyApp.BuildingBlocks/{Observability,Resilience,Security}/
├── infra/terraform/{modules/,environments/{dev,prod}/}, infra/monitoring/{grafana,prometheus}/
```

| Aspect | Modular Monolith (Refs 1–2) | Microservices (Ref 3) |
|---|---|---|
| Deployment | One process/container per host project | One container per service |
| Database | Shared DB (schema-per-module) | DB-per-service, no cross-service joins |
| Communication | In-process calls, or one HTTP hop to a sibling process | HTTP via Gateway + async messaging |
| Solution file | One `.sln` | One `.sln` per service, each independently repo-able |
| Shared code | `SharedKernel`/`Contracts` referenced directly or via a shared project | Only versioned NuGet packages — no shared runtime code |
| Scaling | Scale the whole app (or the one extra Web process) | Scale each service independently |

Modulus already has the primitives Reference 3 would need if a team chose to go there by hand
(`Modulus.Sagas`, `Modulus.Outbox`/`Inbox`, `EventBus.RabbitMQ`/`EventBus.Kafka`, per-module
`DbContext` schema isolation) — what it deliberately does not do, and what this plan does not add,
is generate the gateway/`deploy/k8s`/per-service-repo scaffolding itself.

<!-- source: docs/CLI_REVIEW_PLAN.md -->
## CLI review plan: module-owned wiring, Synvro.* rename, modulus-ui package refs

Status: planned, not implemented. Scope: `cli/` (generators, Scriban templates, tests), plus two small framework seams in `src/`.

### Context

Rule: everything module-specific lives inside the module's composition root (`{Module}Module`, in the module's Infrastructure project). That covers service registration, permissions and grants, integration events, GraphQL/gRPC/endpoint contributions, MVC application parts and seeding. The host `{App}.Api/Program.cs` only composes modules (`AddModulus(..., m => m.AddModule<X>())`) and sets up cross-cutting concerns: auth, CORS, rate limits, OpenAPI, correlation, the migrate call, and the base `AddModulusRealtime/GraphQL/Grpc/Webhooks/AiConnector` calls with their `Map*` calls.

Today the CLI breaks this rule. It patches host `Program.cs` per entity. The `Synvro.*` package rename is half done, and UI package references are added after scaffolding, which makes them fragile.

### Part A. Move module-specific wiring into the module

Current state: `cli/Templates/module/Infrastructure/Module.sbn` registers only the DB, `IUnitOfWork`, events, the repository and mediator handlers.

| Today (host Program.cs / CLI) | Move to |
|---|---|
| `ApiPermissionWiring.EnsurePermission` (`AddPermissions`, Admin `AddPermissionGrants`), called from `GenerateCrudCommand.cs:359-377` | `{Module}Module.ConfigureServices`. Both are plain `IServiceCollection` extensions that accumulate. The host keeps `AddModulusAuthorization()` and `AddGrantStorePermissionChecker()`. |
| Example-module permission block, `cli/Templates/app/Program.sbn:159-167` | Same as above, in the example module template. |
| `AiWiring.EnsureIndexerGrant` (`AiWiring.cs:110-125`, called from `GenerateCrudCommand.cs:277`) | Module `AddPermissionGrants(...GrantToRole("AiIndexer", ...))`. |
| `RealtimeWiring` per-entity `realtime.AddEvent<T>` | Module `ConfigureServices`. |
| `WebhooksWiring` per-event `webhooks.AddEvent<T>` and the `webhooks:manage` grant | Module (event adds and grant). The host keeps `AddModule<WebhooksModule>` and `MapModulusWebhooks`. |
| Presentation-DLL discovery by glob (`Program.sbn:261-264`, `GraphQLWiring.cs:51`, `GrpcWiring.cs:65`) | Module registers its own Presentation assembly. |
| Bare `AddControllers()` in host (`Program.sbn:140`) | Module calls `AddControllers().AddApplicationPart(typeof(...).Assembly)`. Pattern already exists in `identity/IdentityModule.sbn:37`. |
| `SeedIdentityAsync` / `SeedTenancyAsync` in host (`Program.sbn:229-231`) | Module `InitializeAsync`, which gets a service provider. The host only migrates. |

Stays in the host: the `AddModule<...>` list (`AddModuleCommand` keeps appending to it), migrate calls, auth and OpenIddict, tenancy resolvers, the base `AddModulusRealtime/GraphQL/Grpc/Webhooks/AiConnector` calls with their `Map*` calls, and ProjectReferences.

#### Framework seams to add (in `src/`)

1. **Per-module event registration for Realtime and Webhooks.** Add public `services.AddRealtimeEvent<T>(audience, payload?)` and `services.AddWebhookEvent<T>(desc?, payload?)`. The builders' constructors are internal today (`src/messaging/Modulus.Realtime/RealtimeServiceCollectionExtensions.cs:14,93`; `src/messaging/Modulus.Webhooks/WebhooksServiceCollectionExtensions.cs:24,77`), so a module has to pass the whole configuration. New public API goes in `PublicAPI.Unshipped.txt`.
2. **Module-contributed assembly registries.** Add `services.AddModulusEndpointAssembly(asm)`, consumed by `MapModulusEndpoints()` when called with no arguments (`src/core/Modulus.AspNetCore/Endpoints/EndpointDiscovery.cs:29`). The gRPC equivalent goes next to `MapModulusGrpc` (`src/platform/Modulus.Grpc/Server/GrpcServerExtensions.cs:84`). GraphQL already supports this through `AddGraphQLContributor<T>` (`src/platform/Modulus.GraphQL/GraphQLServiceCollectionExtensions.cs:128`), so the CLI only needs to use it. Keep the explicit-assembly overloads.
3. **AI connector:** no seam needed. It reads handler registrations lazily, and the module's `AddMediatorHandlers` already registers the capabilities.

Ordering is safe. Module `ConfigureServices` runs inside `AddModulus` before the host's `AddModulusX` calls, and Realtime and Webhooks run their `configure` callback on every call.

#### CLI changes

- Add a `ModuleWiring` helper that edits `{Module}Module.cs` idempotently and is CRLF-aware, following the existing `ProgramEditor` patterns. Repoint `ApiPermissionWiring`, `AiWiring.EnsureIndexerGrant`, `RealtimeWiring`, `WebhooksWiring`, `GraphQLWiring`, `GrpcWiring` and `GenerateCrudCommand` at it.
- `Module.sbn`: add the permission and grant block, `AddControllers().AddApplicationPart`, endpoint, GraphQL and gRPC assembly registration, and an `InitializeAsync` stub for seeding. Update the `Endpoint.sbn` header comment (lines 7-9), which tells users to grant roles "in Program.cs".
- `Program.sbn`: remove the example-module permissions (159-167) and the Presentation DLL scans. Call `MapModulusEndpoints()` with no arguments.
- `identity/IdentityModule.sbn`, `tenancy/TenancyModule.sbn`, `audit/AuditModule.sbn`: move seeding into `InitializeAsync`. Keep resolvers and OpenIddict in the host as cross-cutting concerns.
- Existing apps that are wired in the host keep working. New commands should detect wiring in either place so they do not duplicate it.
- Tests that assert `Program.cs` content need updating. They are in `tests/unit/Modulus.Cli.Tests/`: `ApiPermissionTests`, `AiCommandTests`, `RealtimeCommandTests`, `WebhooksCommandTests`, `GraphQLCommandTests`, `GrpcCommandTests`, `AuditStoreCommandTests`, `IdentityBackendTests`, `MultiTenancyTemplateTests`. Also check `UiAccessGates.GrantCall` and the `Ui*Tests` files.

### Part B. Finish the Synvro.* rename

These still use the old IDs:
- `cli/Commands/GenerateGrpcCommand.cs:70` (`Modulus.Grpc`)
- `cli/Services/GraphQLWiring.cs:14`, `RealtimeWiring.cs:12`, `AiWiring.cs:13,14,17`
- `cli/Services/VersionCheckService.cs:12,79` (should be `Synvro.Modulus.Cli`, so the self-update check finds the tool)
- The `--framework-only` filters in `OutdatedCommand` and `UpdateCommand`
- `ProjectFileService.cs:~106` (framework-version detection)
- Messages in `NewAppCommand.cs:645` and `Models.cs:123,127`

`AddBffCommand.cs:82` and `DoctorCommand.cs:248` use substring checks and probably work, but verify them. Grep the templates for unprefixed `Include="Modulus.`.

### Part C. modulus-ui package references (0.9.0)

Seven libraries plus `Synvro.Modulus.Ui.Templates`, all at 0.9.0, all from `E:\Personal\framework\modulus-ui`.

- `cli/Templates/app/web.csproj.sbn`: add `Synvro.Modulus.AspNetCore.Mvc`, `Ui.Abstractions`, `Ui.Assets` and `AspNetCore.Hosting` at `{{ui_kit_version}}`. That variable is already passed at `NewAppCommand.cs:445` but no template uses it. Keep `AddUiFrameworkPackages` (around line 633) as a manifest-driven top-up.
- When UI packages can't be resolved for a web kind, fail with an error at `NewAppCommand.cs:645` instead of a warning.
- `UiFrameworkPackages.LocalFeed()`: also try `~/.modulus/nuget-feed` and `../modulus-ui/nupkg`.
- Single source of truth for versions: `UiTemplatePackage.DefaultVersion` (0.9.0) against `ui-kit.json`, and `FrameworkVersion` (1.4.0) against `build/Modulus.Packaging.props`. Add a test that they agree.
- MVC engine gap: `Program.Web.sbn` needs `AddControllersWithViews()` and `MapControllers()` when the engine is mvc. Without these, the MVC controllers are unreachable (AGENTS.md open defect).

### Part D. Housekeeping

- Delete the stray `cli/Commands/NewAppCommand.cs.bak`.
- Fix the mojibake in `NewAppCommand.cs` comments.
- Update AGENTS.md: add the module-owned wiring rule, correct `--kind api|web` to `api|webapp+api` (lines around 159 and 848), correct the samples line (around 843) to `Synvro.Modulus.Ui.*` 0.9.0, and add `ui create-crud` and `ui add-assistant` to the command list.

### Verification

1. `dotnet build modulus.slnx` with 0 warnings and 0 errors.
2. `dotnet test modulus.slnx --filter "Category=Unit"`.
3. `dotnet format modulus.slnx --verify-no-changes`.
4. Generate a real app: `modulus app Demo --kind api --auth openiddict`, then run `generate-crud`, `add-realtime`, `add-webhooks`, `generate-graphql`, `generate-grpc` and `add-ai`.
   - Program.cs should contain no per-entity permission or event lines.
   - The module file should contain them.
   - The build should have 0 warnings, and the generated tests should pass: permissions 401/403/200, realtime, webhooks, GraphQL.
5. Run the same commands twice to confirm they are idempotent.
6. Run the commands on an app that was wired in the host, to confirm no duplicates appear.
7. For `webapp+api` with `--ui-engine mvc` and `razor-pages`, pack modulus-ui, set `MODULUS_LOCAL_FEED` to it, then build and run.

### Assumptions and open questions

- `samples/` is out of scope and keeps its current UI 1.4.0 references.
- Blazor and Fluid web hosts stay unsupported, because modulus-ui itself has no app host for them yet.
- Backward compatibility: apps already wired in the host keep working.
- The framework seams in Part A touch `src/` and need their own review and PublicAPI updates.

<!-- source: docs/ERP_IDENTITY_GAPS_PLAN.md -->
## ERP identity gaps: plan and status

> **Status: complete (2026-10-11).** Every planned item is built. The remaining "Not built" gaps are listed below.
> Production-review fixes in this area are tracked in [`PRODUCTION_READINESS_PLAN.md`](#production-readiness-review-findings-and-plan).

Scope: backend and API only (UI skipped for now). Target: a garments ERP (multi-factory group, merchandisers, buyers, suppliers, shop-floor lines).
Verdict from the analysis: authorization is enterprise grade; the gaps were in authentication plumbing, shop-floor and external-party access, and governance data that lived only in code.

Nothing here is committed. Do not commit unless asked.

### Status

| # | Item | Status |
|---|------|--------|
| 1 | Password and lockout policy, history, expiry | Done |
| 2 | Shop-floor sign-in (employee code + PIN, `Identity:ShopFloor`) | Done |
| 3 | SoD rules as data (`/authorization/sod/rules`, `EfSodRuleStore`) | Done |
| 4 | Bulk import without email | Done, tested (Identity 285) |
| 6a | Composite roles (`/authorization/roles/inclusions`, `EfRoleInclusionStore`) | Done, tested |
| 6b | Positions (`/authorization/positions`, `EfPositionStore`) | Done, tested (Management 67/67, EF 46/46) |
| 7 | External-party users (`/authorization/parties/*`, `EfPartyStore`) | Done, tested (Management 71/71, EF 46/46) |
| 8 | SCIM 2.0 (Users, Groups) | Done, tested (Identity 215/215, 14 SCIM tests) |
| 9 | Multi-step approval workflow | Done, tested (Management 91/91; 10 workflow tests) |
| 10 | Passkeys (WebAuthn) and step-up authentication | Done, tested (Identity 222, Management 95) |
| 14 | HTTP-level tests of the token endpoint | Done (`TokenEndpointHttpTests`, 13 test methods; includes the shop-floor sign-in) |
| 11 | Session metadata, concurrent-session cap, idle timeout per role | Done, tested (Identity 242; `SessionHttpTests`) |
| 13 | Device-code flow and back-channel logout | Done, tested (Identity 252; `DeviceCodeHttpTests`, `BackChannelLogoutHttpTests`) |
| 12 | Tier 3: SAML 2.0 SP, more than one IdP, LDAP/AD bind | Done, tested (Identity 281; LDAP against a fake directory, not a live server) |

### Item 7: external-party users (as built)

Design (all in the authorization packages, no Identity change):
- `PartyLinkRow` (`ModulusPartyLinks`): one account links to one party, with a kind (`buyer`, `supplier`, `subcontractor`) and a `PartyId` (Guid). Linking writes the user's assignment (type = kind, target = party id), so `AddScopeMap<T>(m => m.AssignedKey("buyer", ...))` plus an `assigned:buyer` grant cover exactly that party; relinking moves it, unlinking removes it.
- `PartyCeilingRow` (`ModulusPartyCeilings`): the permissions a kind may ever use (exact or `module:*`). `EfPartyStore.ApplyCeiling` runs at the end of `EfPermissionGrantStore.GetGrants`: a linked account keeps its denies plus grants the ceiling covers; a kind with no rows gets nothing (fail closed). Unlinked accounts are unaffected.
- API under `/authorization/parties`: `GET/PUT links`, `DELETE links/{userId}`, `GET/POST/DELETE ceilings`. Widening a ceiling needs `authorization:grant-any` and refuses `*` and `authorization:*`.
- Tests: ceiling filtering (denies survive, unlinked untouched, `module:*`), no-ceiling fails closed, assignment write/move/remove, grant-any and forbidden-pattern checks.

Limits: delegations to a party account bypass the ceiling; approval limits are not capped by it; in-memory stores ignore parties; existing apps need a migration for `ModulusPartyLinks` and `ModulusPartyCeilings`; creating the party account itself (invitation, role) stays in the app via `IUserInvitationService`.

### Built items, in order (details)

#### Item 8: SCIM 2.0 (as built)
`AddModulusScim<TUser>(configuration)` + `MapModulusScim("/scim/v2")` in `Modulus.Identity` (`Scim/`), settings `Identity:Scim` (`ScimOptions`). All endpoints need the Critical permission `scim:manage`: grant it to the integration client's role (`integration:{clientId}`); the client is bound to one company (`tid`), and everything happens in that company (no company = `403`).
- **Users** `GET/POST /Users`, `GET/PUT/PATCH/DELETE /Users/{id}`: list with `filter` (`eq ne co sw ew pr`, `and`/`or`/`not`, parentheses; attributes `id userName externalId emails(.value) name.givenName name.familyName active`; case-insensitive, values bound as parameters, other attributes `invalidFilter`), `startIndex`/`count` (max `MaxPageSize`, 200). PATCH takes `add/remove/replace` in any case, with or without a path (Azure AD style: `"False"` as text, `emails[type eq "work"].value`, a bag of attributes); unsupported attributes (`displayName`, extensions) are ignored so a full profile still syncs; `id/groups/meta` are `mutability` errors. Accounts are created without a password and email-confirmed (the IdP signs people in; `SendInvitation` mails a first-password link). `DELETE` deactivates (default) or deletes (`DeleteRemovesAccount`). New column `ModulusUser.ExternalId` (index with `TenantId`; existing deployments need a migration).
- **Groups = roles.** Only roles listed in `ManagedRoles` (`*` = all; empty = none, so a privileged role is never reachable by accident) are visible. Members are set with `members` add/remove/replace or `members[value eq "id"]`; a member must be an account of the company. A group is never renamed (permissions are granted to the role by name). `POST/DELETE /Groups` create/remove a role only with `AllowRoleCreation` and only for managed names.
- **Seats.** New seam `ITenantMemberProvisioner` (Core), implemented by `TenantManager` and registered by `AddEfCoreTenantStore`: creating or re-activating an account takes a seat (`MaxUsers` over the limit = `409`, a refused create leaves no account), deactivating or deleting frees it. Without the registration there is no limit.
- **Audit and access changes.** Every change records `scim.user.created|updated|deactivated|deleted` / `scim.group.*` in the security audit; role and activation changes reach `IAccessChangeObserver` through `ModulusUserManager`.
- **Also served:** `ServiceProviderConfig`, `ResourceTypes`. **Sorting (2026-10-22):** `sortBy` and `sortOrder` (RFC 7644 §3.4.2.3) on users (`userName`, `externalId`, `name.givenName`, `name.familyName`) and groups (`displayName`); ties break by id; an unknown `sortBy` is ignored, an invalid `sortOrder` is `400 invalidValue`; advertised as `sort.supported: true`. **ETag (2026-10-22):** `meta.version` and the `ETag` header come from the account's or group's concurrency stamp (weak tag `W/"…"`); `PUT`, `PATCH` and `DELETE` honour `If-Match` (`*` or a tag) and refuse a stale one with `412` before anything is written; advertised as `etag.supported: true`. The check runs inside the service on the record it has just loaded, and the store's concurrency token refuses the write if the record changed after that load, so a stale change cannot win. A group's tag covers its role stamp and its sorted member ids, so a membership change moves the tag even when the role stamp is absent. **Bulk (2026-10-22):** `POST /Bulk` (RFC 7644 §3.7) applies `POST`, `PUT`, `PATCH` and `DELETE` on `/Users` and `/Groups` in order, each through the same service as its single-resource endpoint, so the same rules, seat limits and audit apply. A `bulkId` names a created resource: later paths use `/Users/bulkId:{id}`, and data uses the string `"bulkId:{id}"`. `failOnErrors` stops at that many failures; the rest are not processed. Limits: 1000 operations, advertised as `maxOperations`/`maxPayloadSize` (1 MiB). The bulk is not one transaction (the RFC does not require it). A `DELETE` deactivates the account unless `DeleteRemovesAccount` is set. **Schemas (2026-10-22):** `GET /Schemas` and `GET /Schemas/{id}` (RFC 7643 §6, RFC 7644 §4) serve the User and Group schema documents, with the attributes the server accepts and returns, their required flags and mutability. Unknown ids are `404`. **Passwords (2026-10-22, decided: not set through SCIM).** A `password` in a create, replace or PATCH is refused with `400 mutability` and nothing is written; the message says the person sets their own password through the invitation link (`IUserInvitationService`). The error never echoes the value. `ServiceProviderConfig` keeps `changePassword.supported: false`. **Timestamps (2026-10-22).** Users carry `meta.created` and `meta.lastModified` from two new nullable columns on `ModulusUser` (`CreatedAt`, `ModifiedAt`). SCIM sets them on create and on every SCIM change; they are null for accounts created another way. Limits: `lastModified` moves only on SCIM writes (a sign-in, a password reset or a lockout does not move it), and groups report no timestamps (a role has no column for them). **Upgrade note:** an app with an Identity migration gets `PendingModelChangesWarning` until it runs `modulus migrate add <Name> --module Identity` and `migrate update`. Not built: groups in the user list response (only on single-user reads), `meta.created/lastModified`, enterprise extension.

#### Item 9: Multi-step approval workflow (as built)
Built inside the existing authorization packages (no new package), like approval authority and delegation: engine and in-memory stores in `Modulus.Platform` (`Authorization/Workflow`), EF rows in `AuthorizationStoreDbContext`, endpoints in `Modulus.Authorization.Management`. `AddApprovalWorkflow(o => ...)` registers the engine (scoped `IApprovalWorkflow`) and the escalation service; `AddEfCoreAuthorizationStores` swaps in the EF stores; `MapModulusApprovalWorkflow("/authorization/approvals")` maps the API.
- **Chains are data** (`ApprovalChain`: document type, amount band `MinAmount..MaxAmount`, optional currency, ordered `ApprovalStep`s). The most specific active chain wins (currency-specific first, then the highest lower bound); no chain = a validation error at submit. A step is open to holders of its `Permission` (so roles, positions and delegations all count) and, with `CheckAuthority`, only within their "up to this amount" limit for it. `RequiredApprovals` needs that many different people. `EscalateAfter` + `EscalationPermission` (set both or neither): when a step waits too long, holders of the escalation permission may also decide it. Chains are validated on save (no `*`, no `authorization:` permissions, at most `MaxSteps`).
- **A request keeps the steps and amount it started with.** Editing or deleting a chain never changes one under way.
- **Rules on every decision:** the requester never approves their own request (`REQUESTER_CANNOT_APPROVE`), a person acts on one step only (`ALREADY_ACTED`), the permission is needed (`NOT_AN_APPROVER`), the amount must be within the limit (`NO_APPROVAL_AUTHORITY` / `APPROVAL_LIMIT_EXCEEDED`), and a finished request answers `409 NOT_PENDING`. Two approvers racing for one step cannot both win (`Version` concurrency token, a loser gets `409`). A reject needs a reason and ends the request; only the requester can cancel.
- **No HTTP submit on purpose.** The app calls `IApprovalWorkflow.SubmitAsync(new ApprovalSubmission(type, id, amount, ...))` from its own handler with the amount read from the stored document; a caller-supplied amount would let anyone skip the higher steps. One pending request per document.
- **API:** chains `GET/PUT/DELETE` (`authorization:manage`); requests `inbox`, `mine`, `document/{type}/{id}`, `{id}`, `{id}/why-not`, `approve`, `reject`, `cancel` (signed in; the engine enforces). A request is visible only to its requester, people who decided, holders of a step permission and authorization administrators (others get 404).
- **Hooks:** `IApprovalWorkflowObserver.OnChangedAsync(request, change)` (`submitted`, `approval-recorded`, `step-approved`, `approved`, `rejected`, `cancelled`, `escalated`) is where an app notifies approvers or updates the document. Every change is in the security audit (`approval.*`). `ApprovalEscalationService` checks every `EscalationCheckInterval` (1 min; `EnableEscalation`); with several replicas the conditional update makes each escalation happen once.
- **Tables:** `ModulusApprovalChains`, `ModulusApprovalRequests` (existing deployments need a migration).
- **Not built:** parallel branches or conditional steps beyond the amount band, a reassign action, reminders before escalation, a UI. The in-memory stores are per instance and not tenant aware (development only).

#### Item 10: step-up authentication and passkeys (as built)
**Authentication claims.** Every token the token endpoint issues now records how and when the person signed in: `amr` (RFC 8176: password `pwd`; with a verified authenticator or recovery code `pwd`, `otp`, `mfa`; shop-floor `pin`; federated `fed`; passkey `hwk`/`swk`, `user`, `mfa`) and `auth_time` (Unix seconds). A refresh or authorization-code redemption keeps the **original** values (`TokenPrincipalFactory.RevalidateAsync`), so an old session can never turn into a fresh one. `PasswordGrantResult.SecondFactorVerified` / `AuthenticationMethods` carry the facts from the validators.
**Step-up** (`ModulusAuthorizationOptions.StepUp`, `Modulus.Authorization.StepUpPolicy`): `RequireForCritical` (default **off**: tokens issued before this carry no claims) and/or `Permissions` name permissions that need `amr` `mfa` and an `auth_time` within `MaxAge` (15 min). `PermissionRequirementHandler` fails such a requirement even when the permission is held, and `StepUpResultHandler` answers per RFC 9470: `401`, `WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="mfa", max_age="900"`, problem code `STEP_UP_REQUIRED` (a plain missing permission stays `403`). The client re-authenticates (password grant with `mfa_code`, or a passkey) and retries. Machine principals (role prefix `integration:`) are exempt: they have no second factor, grants limit them. Unauthenticated-claim tokens from other IdPs fail closed once the option is on, so enable it where your tokens carry `amr`/`auth_time` (the local OpenIddict server does).
**Passkeys** (`AddModulusPasskeys<TContext, TUser>(configuration)`, `MapModulusPasskeys()`, settings `Identity:Passkeys`, package `Fido2` 4.2, MIT; `Microsoft.Bcl.Memory` pinned to 9.0.20 for GHSA-73j8-2gch-69rq):
- Discoverable credentials, **user verification always required** (so a passkey is possession + biometric/PIN and its token carries `mfa`), attestation `none`, origins and relying-party id from settings (a wrong origin, a missing UV flag, a counter that goes backwards = cloned authenticator are all refused).
- Ceremonies live in `ModulusPasskeyChallenges` and are consumed with one conditional delete: a signed answer cannot be replayed, any replica can finish what another started, expired ones are swept as new ones start.
- Endpoints (`/account/passkeys`, covered by the sensitive rate limit): `POST login/options` (anonymous, framework loosening), then the answer goes to `/connect/token` with `grant_type=urn:modulus:params:oauth:grant-type:passkey`, `challenge_id`, `assertion`. Signed in: `POST register/options`, `POST register/complete`, `GET`, `DELETE {id}`. Adding or removing needs a sign-in within `ManageMaxAgeMinutes` (15; a token without `auth_time` is refused) and at most `MaxPasskeysPerUser` (10); a ceremony can be finished only by the account that started it.
- Tables `ModulusPasskeys` (credential id unique, cascade with the account) and `ModulusPasskeyChallenges` (existing deployments need a migration). Tested against the real Fido2 verifier with a software authenticator (real ES256 attestation and assertions).
- Not built: attestation/metadata checks (MDS), passwordless-only accounts (a passkey is an additional way in; disabling the password is a separate step), a UI to manage passkeys, a cookie-login (`/account/login`) passkey step.

#### Item 14: HTTP-level token endpoint tests (as built)
`tests/unit/Modulus.Identity.Tests/TokenEndpointHttpTests.cs` boots a real OpenIddict server and validation stack on a TestServer (SQLite, development certificates, unencrypted JWT access tokens, `https://` base address because OpenIddict refuses plain HTTP) and calls `/connect/token` and `/connect/authorize` the way a client does, then uses the issued bearer tokens against protected endpoints. Covered: password grant (claims `sub`/`amr`/`auth_time`, bearer use, wrong password / unknown user / disabled account answer the same `invalid_grant`), a client without the grant permission, two-factor (`mfa_code` required, wrong code refused, token says `pwd`+`otp`+`mfa`), refresh (keeps `auth_time`/`amr`; refused after a security-stamp change and after the account is disabled), shop-floor (registered device only, `invalid_client` 401 for an unbound client, no refresh token, only floor roles, `tid`), passkey (register with a recent token, sign in, replay refused, refresh keeps `hwk`/`user`/`mfa`, missing parameters `invalid_request`), **step-up end to end** (a password-only bearer token gets `401 insufficient_user_authentication` on a Critical endpoint, a passkey token passes), authorization code + PKCE (login redirect, `prompt=none` → `login_required`, code redeemed once, wrong verifier refused, missing `code_challenge` and an unregistered redirect URI never produce a code), federated exchange (account created, same subject returns to it, verified-email link, bad token and disallowed email domain refused alike, missing token `invalid_request`). Test helpers: `SoftwareAuthenticator` (real ES256 WebAuthn attestations/assertions).
Not covered over HTTP: client-credentials, the introspection and userinfo endpoints, revocation (`/connect/revoke` has its own handler tests), federated tokens read from a real provider's JWKS (a fake `IFederatedTokenReader` stands in).

#### Item 11: session registry (as built)
Settings `Identity:Sessions` (`SessionOptions`, **off by default**); registered by `AddModulusIdentity` when `Enabled` (`ISessionRegistry`, table `ModulusSessions`, existing deployments need a migration).
- **A session is a sign-in.** Every interactive grant (password, passkey, federated, shop-floor, and an authorization code at redemption) starts one and puts its id in the tokens as `sid` (a refresh keeps it). It records the client, roles, `amr`, the caller's address, `User-Agent` (shortened) and an optional `device_name` token request parameter. Behind a proxy the address is right only if forwarded headers are configured (`ApplyModulusTrustedProxies`).
- **Enforced on every request.** `SessionValidationHandler` (an OpenIddict validation handler after the token-entry check) rejects an access token whose session ended or timed out (`invalid_token`), and a refresh of an ended session is `invalid_grant`. So ending one session stops that device's access and refresh tokens at once, without needing token-entry validation or a token-to-session link. Tokens without `sid` (issued before it was switched on) are not affected, and a refresh token from before it starts a session at its next refresh.
- **Concurrent-session cap.** `MaxConcurrent` plus `MaxConcurrentByRole`: the strictest limit that applies to the person's roles. `OnLimit`: `EndOldest` (default; the session used least recently ends, reason `limit`) or `Refuse` (the new sign-in gets `invalid_grant` with a message to end a session first). The count is soft under a sign-in race (two simultaneous sign-ins can both pass).
- **Idle timeout.** `IdleTimeoutMinutes` plus `IdleTimeoutMinutesByRole` (strictest wins, decided by the roles at sign-in). The last-seen time is written at most every `TouchIntervalSeconds` (60), so the timeout is accurate to that; a timed-out session ends with reason `idle-timeout` on its next use or when the next sign-in counts sessions.
- **For the account holder:** `GET account/sign-ins` (where from, device, browser, last used; the current one is marked), `DELETE account/sign-ins/{id}`; `POST account/sessions/revoke-all` and `IUserSessionService.RevokeAllAsync` also end every registry session. All changes go to the security audit (`session.started|ended|refused`). Ended sessions are kept `RetainEndedDays` (30) and removed as new sessions start.
- **Not built:** an admin API/page to list or end another person's sessions (call `ISessionRegistry`), new-device / impossible-travel detection, geo-lookup of the address, sessions for client-credentials tokens and for the Identity cookie login, a distributed cache in front of the per-request session lookup (one primary-key read per request).

#### Item 13: device-code flow and back-channel logout (as built)
**Device authorization grant (RFC 8628)** — `Identity:AllowDeviceCodeFlow` (off by default). OpenIddict serves `POST /connect/device` (a short `user_code`, a `device_code`, `verification_uri` `/connect/verify`, poll `interval`); the device polls `/connect/token` with `grant_type=urn:ietf:params:oauth:grant-type:device_code` (`authorization_pending` until approved, `access_denied` if refused, `expired_token` after the code lifetime). A client needs the device-authorization endpoint and device-code grant permissions.
- **Approval is a bearer call, not a cookie page.** A person already signed in elsewhere sends the code to `/connect/verify` with their **own access token**: `GET ?user_code=` says which client and scopes are being authorized; `POST` form `user_code` + `decision=approve|deny` decides (a POST reads the form only, a GET the query). No cookie, so no cross-site request can approve a device. Approval needs a sign-in within `DeviceApprovalMaxAgeMinutes` (15) (`401 recent_sign_in_required`), an unknown, expired or used code is `404 invalid_user_code`, no token `401`.
- **What the device gets:** the approver's identity (re-verified: active, not locked, current roles), scopes limited to the allowed ones, the approver's `amr` and `auth_time` (so an MFA approval gives an MFA device token; refresh keeps them), and **a session of its own** (the approver's `sid` is removed; the device starts a session at its first poll, so caps, idle timeouts and back-channel logout apply to it separately). The verification endpoint is on the sensitive rate-limit paths with `/connect/device`.
- **Found on the way (fixed):** the session and passkey services called `ChangeTracker.Clear()` on the `DbContext` that OpenIddict's token store shares, which detached the code entry OpenIddict was about to redeem and made every one-time code (authorization code, device code) fail as "already redeemed" once sessions were on. They now detach only their own rows. Also, `auth_time` and the approval freshness check now use the host's `TimeProvider`.
- Not built: a browser page for entering the code (the app's UI calls the bearer endpoint; or approve from a mobile app), `verification_uri_complete` QR flow, per-client lifetimes, a way to limit which clients may use it beyond OpenIddict permissions.

**Back-channel logout (OpenID Connect Back-Channel Logout 1.0)** — `Identity:Sessions:BackChannelLogout` (needs the session registry and `Identity:Issuer`, which is the token's `iss`; startup fails without it).
- An application registers its address with `IBackChannelLogoutDirectory.SetAsync(clientId, uri)` (kept in the OpenIddict application's properties; https only, or http for loopback / `AllowInsecureUris`; no credentials or fragment). When one of its sessions ends (sign-out, `DELETE account/sign-ins/{id}`, sign out everywhere, a cap pushing it out, an idle timeout), a row goes into `ModulusLogoutNotifications` and `BackChannelLogoutService` POSTs `logout_token=<JWT>` (`typ` `logout+jwt`, `iss`, `aud` = client id, `sub`, `sid`, `jti`, `iat`/`exp` 120 s, `events`; **no nonce**), signed with the server's signing key, so the application verifies it against the JWKS. The session id is also in the **ID token** (`sid`) so the application can match it.
- Delivery is queued in the database: claimed with a conditional update (two replicas never send the same one), retried with doubling backoff (`RetryBackoffSeconds` 30, `MaxAttempts` 6), abandoned with the last error kept, redirects not followed, delivered and abandoned rows purged after `RetainDays` (14). Only 200/204 count as acknowledged.
- Not built: the discovery metadata flags (`backchannel_logout_supported`, `backchannel_logout_session_supported`), front-channel logout, RP-initiated logout (`end_session_endpoint`), a private-network guard on the registered address (it is set by an administrator), an admin API to see the queue (query the table).

#### Tier 3 (remaining)
12. SAML 2.0 SP (BSD-licensed library such as `ITfoxtec.Identity.Saml2`), more than one IdP per app, LDAP/AD bind (`System.DirectoryServices.Protocols`).

#### Item 12: SAML 2.0 service provider, several identity providers, LDAP/AD bind (as built)
Both live in `Modulus.Identity` and share one account-provisioning step (`ExternalAccountProvisioner<TUser>`): the outside system's stable id is linked to an account through ASP.NET Identity's `AspNetUserLogins` (`ldap:{directory}` / `saml:{provider}`, so renaming a person in the directory does not create a second account); a first sign-in links an existing account by e-mail only when `LinkExistingByEmail` is on, creates the account (no password, e-mail confirmed, a seat taken through `ITenantMemberProvisioner`; `409` over the limit leaves no account) only when `AutoProvision` is on, and otherwise refuses. Disabled or locked-out accounts are refused. At every sign-in the profile (name) is refreshed and **roles follow the groups**: the person gets the roles of the groups they are in and loses the mapped roles of groups they left; roles that are not a value in `GroupRoles` are never touched; a mapped role that does not exist is skipped with a warning. An e-mail address is required (Identity enforces unique addresses).

**LDAP / Active Directory** (`AddModulusLdap<TUser>(configuration)` after `AddModulusIdentity`, settings `Identity:Ldap`): wraps the password-grant validator. For a typed user name it asks each directory in order (service-account search with `UserFilter`, `{0}` escaped per RFC 4515) and, for the first that has exactly one such person, proves the password by **binding as that person**; nothing is stored locally. A user name no directory knows goes to the wrapped validator (local passwords: break-glass admin) unless `AllowLocalFallback` is off; a name a directory knows never falls back, and an unreachable directory refuses everyone (no silent fallback to a local password). An empty password is refused before any bind (it would be an anonymous, successful bind). TLS: startup refuses a non-local host with neither `UseSsl` nor `UseStartTls` unless `AllowInsecure`; referrals are never chased. A local second factor on the account still applies (`mfa_code`). Several directories (`Directories[]`), each with its own company, attributes and `GroupRoles`. `ILdapDirectoryClient` is replaceable (default `System.DirectoryServices.Protocols`; Linux needs libldap). Denials go to the security audit (`signin.directory.denied`, reason code, no names).

**SAML 2.0** (`AddModulusSaml<TContext,TUser>(configuration)` + `MapModulusSaml()`, settings `Identity:Saml`, library `ITfoxtec.Identity.Saml2`, BSD-3): SP-initiated web SSO, redirect binding out, POST binding back. `GET {base}/{provider}/login?returnUrl=` (local paths only) saves a one-shot request (`ModulusSamlRequests`: relay state, request id, provider, return URL, expiry) and redirects; `POST {base}/{provider}/acs` consumes it first (a replay, an answer nobody asked for or one for another provider finds nothing) and then accepts the response only if: signed by one of that provider's **pinned** certificates (no chain trust), assertion conditions and audience hold (audience = our entity id), `Issuer` equals the provider's entity id, `InResponseTo` equals our request id, `Destination` equals the ACS URL built from `BaseUrl` (never the `Host` header). Success writes the normal Identity cookie with `amr` `fed` (+ `mfa` when the assertion's `AuthnContextClassRef` is in the provider's `MfaAuthnContexts`; default none), so `/connect/authorize` then issues tokens as for a password sign-in (`ModulusAuthorizeController.BuildPrincipal` now reads the cookie's `amr` instead of assuming `pwd`). Also `GET {base}` (provider list for login buttons) and `GET {base}/{provider}/metadata`. Several providers per app, each with its own certificates, company, attribute names, group map. Rate limit sensitive paths now include `/saml`.

Tests: `LdapTests` (14, fake directory: provisioning, rename, wrong password, empty password, fallback on/off, no fallback for directory users, unreachable directory, group sync, auto-provision off, disabled account, link by e-mail, filter escaping, TLS rule) and `SamlHttpTests` (15, a simulated IdP signing with ITfoxtec on a real TestServer host: full flow to an authorization-code token, repeat sign-in, replay, wrong signer, tampered and unsigned responses, unknown/other request, cross-provider, wrong audience, expired request, MFA context, missing attributes, open-redirect guard, metadata).

Not built / limits: IdP-initiated SSO (unsolicited responses are refused on purpose), single logout, encrypted assertions are configured but untested, artifact binding, IdP metadata URL refresh (certificates are configured by hand), `auth_time` is when this server signed the person in (an old IdP session reused without `ForceAuthentication` counts as fresh), nested AD groups (`memberOf` lists direct groups only), LDAP password change/expiry, the real `LdapConnection` has not been run against a live server (only the fake). The login page (`Modulus.UI.Identity`) has no SAML buttons yet: link to `GET {base}/{provider}/login?returnUrl=`. New table: `ModulusSamlRequests`.

#### Item 4: bulk import without e-mail (as built)
`IShopFloorAccountService.ImportAsync(tenantId, rows)` (`ShopFloorImportRow`: code, names, roles, optional initial PIN) creates one operator account per line and reports each (`ShopFloorImportResult`: account id, the initial PIN, or the error). A line with no PIN gets a random one of the minimum length (not a repeated digit); it is returned once for printing and stored only hashed. Every imported account has `MustChangePin` (new column on the user table): the shop-floor grant answers `pin_change_required` for the right PIN until the operator sends `new_pin` with it; the new PIN must be acceptable and different, then the old one stops working and a token is issued in the same request. `SetPinAsync(userId, pin, mustChange: true)` does the same for an administrator reset. Duplicate codes (in the company or within the file), unknown roles and bad codes fail their line only. Parsing the CSV/Excel stays in the app. Tests: 3 in `ShopFloorTests`, 1 HTTP test. New column: `MustChangePin`.

### Open gaps from finished items (carry forward)
- Password history is recorded regardless of the policy switch; the cookie login ignores password expiry; no breached-password (HIBP) check.
- Role inclusions and positions apply only through the EF grant and approval stores; the in-memory stores, `IUserRoleDirectory` and claims-based role checks do not expand them.
- Adding an inclusion, a holding or an SoD rule does not warn about a resulting violation; SoD is evaluated on the resulting permissions.
- A position's `OrgUnitId` is stored but not enforced.
- Position holdings and inclusions are matched case-insensitively by role name.

### Migrations existing apps will need
`ModulusSodRules`, `ModulusRoleInclusions`, `ModulusPositions`, `ModulusPositionAssignments`, `ModulusPartyLinks`, `ModulusPartyCeilings`, `ModulusApprovalChains`, `ModulusApprovalRequests` (authorization store); `ModulusPasswordHistory`; `ModulusPasskeys`, `ModulusPasskeyChallenges`, `ModulusSessions`, `ModulusLogoutNotifications`, `ModulusSamlRequests` (identity database); `EmployeeCode`, `PinHash`, `ExternalId` and `MustChangePin` columns on the user table.

### Docs
`AGENTS.md` (Security model section) carries the as-built summary of every item; `docs/SECURITY_STAGE2_PLAN.md` points here for the ERP identity work. All twelve items are done.

### Checks per item
`dotnet build modulus.slnx` (0 warnings), `dotnet test modulus.slnx --filter "Category=Unit"`, `dotnet format modulus.slnx --verify-no-changes`; new public API goes in `PublicAPI.Unshipped.txt` (net10 only).

<!-- source: docs/PRODUCTION_READINESS_PLAN.md -->
## Production readiness: review findings and plan

**Status (2026-10-11): in progress, not production-ready.** Steps 0 through 4 are done, and the CHANGELOG is written. The status becomes "complete" once the gates under *Verification* that have not run yet pass. Tick each box when its fix and test land. Set this status to "complete" once every gate in
*Verification* passes.

Scope: everything in the repository except `samples/`.

Decisions taken:
- The unused Phase 5–9 CLI work is deleted from this repository. UI work belongs in `modulus-ui`.
- Security fixes that change defaults ship as **breaking, secure-by-default** changes. Each is recorded in `CHANGELOG.md` together with its opt-out.

### Gate results

| Gate | Before (review) | Now |
|---|---|---|
| `dotnet build modulus.slnx -c Release` | **FAIL**: 36× RS0016 in `cli/Services/FormStateService.cs` | **PASS**: 0 warnings, 0 errors |
| `dotnet format modulus.slnx --verify-no-changes` | **FAIL** (same compile errors, plus 41 whitespace violations in the CLI) | **PASS** after `dotnet format whitespace` on the CLI and its tests |
| `dotnet test modulus.slnx --filter Category=Unit` | not runnable | **1,701 passed, 6 failed**: see T1 and T2. Re-run 2026-10-12: all projects green except `EvictableFixedWindowLimiterTests.EvictIdlePartitions_RemovesOnlyIdlePartitions`, which failed once under parallel load and passes 3/3 alone (timing-based, not fixed). `Modulus.Cli.Tests` 530/530 after fixing the resolver (below) |
| `dotnet build` (Release, 2026-10-12) | PASS | PASS: 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` (2026-10-12) | PASS | PASS (exit 0) |
| `dotnet list package --vulnerable --include-transitive` (2026-10-12) | PASS | PASS: no vulnerable packages |
| `dotnet list package --vulnerable --include-transitive` | PASS, none | PASS |
| `dotnet build modulus.slnx -c Release` (2026-10-22) | PASS | PASS: 0 warnings, 0 errors |
| `dotnet format modulus.slnx --verify-no-changes` (2026-10-22) | PASS | PASS (exit 0) |
| `dotnet test modulus.slnx --filter Category=Unit` (2026-10-22) | PASS | PASS: 71 test assemblies, 0 failed, net8.0 and net10.0 |
| `dotnet pack modulus.slnx -c Release` (2026-10-22) | PASS | PASS: Synvro.Modulus.* 1.5.0, Cli 1.5.5 |
| Generated `api --auth openiddict --multi-tenancy` app, tests from the local feed (2026-10-22) | PASS | PASS: 11/11, including `SecurityProbeTests`. **Fails against the published nuget.org 1.5.0 packages**: see the open item below |
| Mongo, EF Core and RLS integration tests (Docker or Podman) | not run | Run 2026-10-23 (Podman): EF Core 46/46 and 49/49, Mongo 6/6 |
| TODO / FIXME / `NotImplementedException` in `src/` | none | none |

### Step 0: Plan docs ✅

- [x] This document created and linked from `AGENTS.md` ("Production readiness (open work)") and `PROJECT_STRUCTURE.md`.
- [x] Deleted as obsolete:
  - `cli/PHASE_*.md` (28 files) and `cli/CLI_PRO_SCAFFOLDING.md`, which claimed a "v3.5.0" release (the real version is 1.4.0, `build/Modulus.Packaging.props`);
  - `docs/UI_FRAMEWORK_PLAN.md` and `docs/UI_FRAMEWORK_GUIDELINE.md`, which described the removed `src/ui/`.
- [x] Plan docs were consolidated into this file (commit `bdded52`, and `c9d76f2` for the readiness plan). The separate files were deleted, so the archive and status-line work above no longer has files to point at:
  - `docs/archive/IMPROVEMENT_PLAN.md`, `DUAL_TARGETING_PLAN.md` and `ROADMAP_TIER3.md`: content kept in the sections of this file, not as separate archive files.
  - `docs/ERP_IDENTITY_GAPS_PLAN.md`: its content is the "ERP identity gaps" section of this file.
  - `docs/APP_KIND_SPLIT_AND_IDENTITY_UI_PLAN.md`: its content is the "App-kind split" section of this file; Phase B moved to `modulus-ui`.
- Not live as separate files: `docs/ADVANCED_FEATURES_PLAN.md` and `docs/SECURITY_STAGE2_PLAN.md`. Their content is in this file (the "Security stage 2" section and the advanced-features sections).

### Step 1: Unblock the build ✅

The Phase 5–9 commits of 2026-10-11 (f294a0a … e76ff65) added code that no CLI command, template, csproj item or test referenced:
- [x] Deleted 6 C# files in `cli/Services/`:
  - `FormStateService.cs`: public, in namespace `Modulus.Ui.Services`; this file broke the build;
  - `FormValidationService.cs`, `BuiltInValidators.cs`, `ValidationModels.cs`, `ValidationResultService.cs` and `AsyncValidationService.cs`.
- [x] Deleted about 20k lines of TypeScript with no `package.json` or `tsconfig`, never compiled, tested or packed:
  - `cli/wwwroot/`;
  - `cli/Tests/*.ts`;
  - `cli/Services/*.ts`;
  - `cli/generate-{page,email}-command.ts`.
- [x] Release build green (0/0), and formatting clean after fixing 41 whitespace violations in the CLI that predate this work.
- [x] **T1.** Five CLI tests labelled `Category=Unit` depend on the machine's template cache or the network:
  - `UiFileGeneratorTests.GenerateAsync_WithValidEngine_CreatesFiles` (×3);
  - `TemplatePackageResolverTests.ValidateTemplatesAsync_WithValidStructure_ReturnsTrue`;
  - `TemplatePackageResolverTests.ResolveAsync_WithCachedPackage_UsesCache`.

  They resolve `Modulus.Ui.Templates` 0.9.0 from `~/.modulus/cache`, the local feed or nuget.org. On this machine the cached
  package resolves but fails `ValidateTemplatesAsync`, so no files are generated. `UsesCache` also asserts on a stopwatch
  (`time2 < time1 / 3`), which fails whenever both calls hit the cache.

  Fix: build a small fixture template package in the test (a temp dir or zip) and point the resolver at it through a
  constructor parameter for the cache root. Replace the timing assertion with "no download on the second call". Move
  any test that really needs the network to `Category=Integration`. Then check whether the cached 0.9.0 package itself
  is invalid; if it is, that is a `modulus-ui` packaging defect.

  *Done:* the tests now build a fixture package (`TemplatePackageFixture`) and inject the cache root and HTTP client into
  `TemplatePackageResolver`. The cause was not the cached package: `ui-kit.json` sits beside `templates/`, but the validator
  looked two levels up and the generator three. Both now read `<version>/ui-kit.json`. Open for `modulus-ui`: the cached
  0.9.0 manifest has CRUD files for `razor-pages` and `blazor` only, so `mvc` generates nothing.
- [x] **T2.** `ApprovalWorkflowTests.Two_approvers_racing_for_one_step_cannot_both_win` is flaky. It failed once in the full
  parallel run with `SQLite Error 5: unable to delete/modify user-function due to active statements`, and passed 3/3 alone.
  Fix: give the test its own connection per approver and register SQLite functions before any statement runs, or put
  the test in a non-parallel collection.

### Step 2: Security (identity, platform, AI)

#### Critical
- [x] **S1. Password-reset mail goes to the typed address, not the stored one.**
  - Where: `src/identity/Modulus.Identity/ModulusAccountController.cs:68-70` (also `:176-178`); `IdentityEmailQueue.cs:38,62`.
  - Problem: `FindByEmailAsync` matches through the upper-cased normalized form, so a lookalike address (`admin@gıthub.com`, dotless ı) finds the victim's account. The token is then mailed to the attacker's address: account takeover.
  - Fix: queue only the user id; the worker sends to the stored `user.Email`. Do the same for the confirmation mail.
  - Test: a lookalike address never receives the mail.

  *Done:* the queue carries the account id only, and the worker mails the stored address. Callers no longer pass an address.
  Test: `AccountLookalikeAddressTests`. It uses a case variant of the address (the dotless-ı lookalike does not match on this runtime's normalizer), which takes the same lookup path.

#### High
- [x] **S2.** `POST account/2fa/setup` replaces the live authenticator key without re-authentication (`UserTwoFactorService.cs:45-58`, `ModulusAccountController.cs:272-275`). A stolen session can take over the second factor.
  Fix: refuse while two-factor is enabled. The owner disables it first, which requires the password.
- [x] **S3.** `/connect/introspect` checks only the signature and lifetime (`ModulusTokenController.cs:636-669,736-789`). Revoked tokens, tokens of ended sessions and `id_token`s come back `active=true`.
  Fix: check the OpenIddict token entry status, the session registry (`sid`) and the token type (access tokens only).

  *Done:* `TokenIntrospectionTests` (3 tests over HTTP). While writing them I found a second defect: introspection compared
  the issuer `https://host` with the token's `https://host/`, so every token failed signature validation. Fixed in the same change.
- [x] **S4.** Open redirect through control characters (`SamlSignInService.cs:289`, `src/platform/Modulus.Bff/Endpoints/BffEndpointRouteBuilderExtensions.cs:136`).
  `/%09/evil.com` passes the check, and the browser strips the tab and lands on `//evil.com`.
  Fix: one shared helper that mirrors ASP.NET `IsLocalUrl` (rejects control characters), used in both places.
- [x] **S5.** SAML and federated sign-in skip the account's local second factor, and `LinkExistingByEmail` trusts an IdP email that may be unverified (`SamlSignInService.cs:143-146`, `ExternalAccountProvisioner.cs:51-59`, `FederatedLoginValidator.cs:50-75`).
  Fix: enforce local 2FA as the LDAP path does, and link by email only when the provider is flagged as asserting verified addresses.
  *Done:* SAML and federated sign-in refuse an account with a local second factor (neither path can answer a code). Test: `ExternalAccountLinkTests`, `FederatedLoginTests`. Test: `SamlHttpTests.A_person_with_a_local_second_factor_is_refused_at_the_provider_sign_in`.
- [x] **S6.** `TenantMiddleware.cs:44-90` checks only `ctx.User` from the default scheme. Membership is not checked in two cases:
  - endpoints that use their own authentication scheme (the BFF client schemes);
  - hosts where `UseMultiTenancy` runs before `UseAuthentication`.

  Fix: re-check membership after endpoint authentication (an authorization requirement or endpoint filter), and refuse the wrong middleware order at startup.

  *Done:* the membership check now authenticates the caller against every registered scheme when the default scheme is anonymous (`TenantMiddleware`), so it no longer depends on where `UseMultiTenancy` sits. The startup order refusal was not added because the order no longer matters. Test: `TenantMiddlewareTests`.
#### Medium
- [x] **S7.** The BFF bearer policy accepts any token from the authority when `Audiences`, `AllowedClientIds` and `ClientId` are all empty (`BffServiceCollectionExtensions.cs:212`). Fix: fail at startup. *(breaking)*
- [x] **S8.** The BFF cookie uses `SecurePolicy = SameAsRequest` (`:236`). Fix: `Always` outside Development. *(breaking)*
- [x] **S9.** The refresh grant fails open when no user manager resolves (`ModulusTokenController.cs:402-407`). Fix: deny.
- [x] **S10.** `2fa/disable` uses `CheckPasswordAsync`, which does not count toward lockout (`ModulusAccountController.cs:297`). Fix: `CheckPasswordSignInAsync(lockoutOnFailure: true)`.
- [x] **S11.** LDAP binds before checking local lockout and does not count failures (`LdapPasswordGrantValidator.cs:64-80`).
- [x] **S12.** `/bff/login` has no rate limit unless one is configured (`BffServiceCollectionExtensions.cs:341`). Fix: a default sensitive-route limiter.
- [x] **S13.** Insecure defaults, changed as breaking:
  - `Storage:IsolateTenants` is now `true` when multi-tenancy is registered (`StorageOptions.cs:19`);
  - `TrustPermissionClaims` is now `false` (`ModulusAuthorizationOptions.cs:21`).
- [x] **S14.** `EfPermissionGrantStore.GetGrants` runs synchronous `.ToList()` queries on every authorization check (`:41-79`, called from `PermissionRequirement.cs:74`). Fix: an async path plus a per-request cache.
- [x] **S15.** The reset-mail queue drops writes silently when full (`IdentityEmailQueue.cs:32-36`, capacity 1000). Fix: log a warning and count drops with a meter.
- [x] **S16.** Lower priority:
  - `RedisIdempotencyStore.cs:52,99` replays a response when the stored fingerprint is empty;
  - `ShopFloorServices.SetPinAsync` has no tenant check of its own;
  - `LocalFileStorage.cs:34` compares paths with `OrdinalIgnoreCase`, which is wrong on Linux.

  *Done:* the shop-floor check reads the caller's company from `ICurrentTenant`; the signature of `SetPinAsync` is unchanged.
Tests: `tests/unit/Modulus.Identity.Tests`, `Modulus.Bff.Tests`, `Modulus.Platform.Tests`.

### Step 3: Data and messaging (High)

- [x] **D1.** Mongo `DeleteRangeAsync` drops `Skip`/`Take` on its server-side paths (`src/data/Modulus.Data.MongoDB/MongoRepository.cs:203-218`). "Delete the oldest 100" deletes every match.
  Fix: when paging is set, resolve the ids through `ListAsync`, then `DeleteMany` with `_id $in`.
- [x] **D2.** The Mongo inbox starts without its unique index `ux_eventid_handlername` when creating it fails (`InboxMongoExtensions.cs:94-97`). Concurrent consumers then process an event twice.
  Fix: fail startup, with an explicit `SkipIndexCreation` opt-out.
- [x] **D3.** The `OutboxDepth` gauge accumulates (`OutboxProcessor.cs:33`, `MongoOutboxProcessor.cs:31`). `_lastReportedDepth` lives on processors registered as scoped, so it is always 0.
  Fix: move it to a singleton tracker.
- [x] **D4.** RabbitMQ defaults to `guest`/`guest` and has no TLS option (`RabbitMqOptions.cs:10-13`, `RabbitMqEventBus.cs:185-197`, `RabbitMqEventConsumer.cs:87-94`).
  Fix: validated options with required credentials, and an `Ssl` section. *(breaking)*
- [x] **D5.** One unexpected Kafka error stops the host (`KafkaEventConsumer.cs:175-181,216,265`).
  Fix: catch per iteration, back off, and close the consumer in `finally`. Reuse one dead-letter producer (`:298-306`) instead of building one per message.
- [x] **D6.** The Mongo repository scans whole collections client-side without a document filter, and ignores the spec's sort when a filter is set (`MongoRepository.cs:84-93,136-138`).
  Fix: always apply the sort before `Skip`/`Take`, and add a `MaxClientSideScan` guard.

  *Done:* the test for sort-before-paging uses the client-side path. Test for the case the fix guards: `Listing_sorts_before_it_pages_when_the_filter_is_translated_but_the_sort_is_not` (fails when the fix is removed).
Tests: `tests/unit/Modulus.Data.MongoDB.Tests`, `Modulus.Outbox.Tests`, `Modulus.EventBus.*.Tests`. For D1, D2 and D6, also run the Mongo integration tests.

### Step 4: Medium (core, messaging, CLI)

- [x] **M1.** `IdempotencyStoreSweeper.cs:64-68` has a bare `catch {}` and no logger. Fix: log a warning.
- [x] **M2.** `InMemoryIdempotencyStore` has no entry limit, and `CompleteAsync`/`AbandonAsync` don't check that the caller owns the claim (`:55-81`).
  Fix: add a claim token and a `MaxEntries` option.
  *Done:* `MaxEntries` bounds the in-process store; a claim token (`IdempotencyResult.ClaimToken`) is handed to `CompleteAsync`/`AbandonAsync` through default interface methods, so existing stores keep working. Test: `A_request_whose_claim_was_taken_over_cannot_complete_or_abandon_the_new_owners_claim`.
- [x] **M3.** `EfInboxStore.cs:263-266,306-309` catches every `InvalidOperationException`. Fix: narrow the catch to the in-memory provider.
- [x] **M4.** RabbitMQ consumer and publisher:
  - reject `AutoAck=true` combined with manual ack/nack;
  - log an error when there is no dead-letter exchange;
  - pass the `CancellationToken` through;
  - reuse publisher channels (`RabbitMqEventBus.cs:60-62`).
- [x] **M5.** `SqlServerRowLevelSecurity.EnsureAsync` drops and recreates policies outside a transaction (`:149-169`). Fix: use a transaction, or `ALTER SECURITY POLICY`.
  *Done:* the rebuild runs in one transaction. The SQL Server RLS integration tests were not run here (they need a SQL Server container).
- [x] **M6.** The Mongo outbox poller has no drain loop and no leader election, and swallows OCE (`MongoOutboxPollingService.cs:31-48`). Fix: mirror the EF `OutboxPollingService`.
  *Done:* no wake-on-commit signal in the Mongo poller (the signal type lives in the EF outbox package and Mongo does not reference it). Test: `The_poller_drains_a_backlog_larger_than_one_batch_without_waiting_for_the_interval` (fails when the drain-again step is removed).
- [x] **M7.** `src/observability/.../OpenTelemetryExtensions.cs:35` is a public no-op placeholder. Fix: implement it, or mark it `[Obsolete]`.
- [x] **M8.** CLI `TemplatePackageResolver.cs`:
  - the download URL is wrong for NuGet v3 feeds (`:202`); fix: use the flat-container URL;
  - downloads are cached with no integrity check; fix: verify the download is a valid package;
  - `catch {}` at `:214`; fix: report the error.

### Step 5: Wrap-up

- [x] `CHANGELOG.md`:
  - a Security section (S1–S6);
  - the breaking defaults (S7, S8, S13, D4) with their opt-outs;
  - the other fixes.
- [ ] **Open, blocks Step 5 until 1.6.0 is published:** the CLI 1.5.5 module templates (`cli/Templates/module/Application/CreateHandler.sbn` and `UpdateHandler.sbn`, added in `e625a0e`) use `Modulus.Authorization.Dimensions` (`IDimensionValueRegistry`) and `Modulus.Authorization.Resources` (`IResourceScopeGuard`), which live in `Synvro.Modulus.Platform`. Checked 2026-10-23 with a fresh download from nuget.org (`Synvro.Modulus.Platform` 1.5.0, 374 KB): those types are **not** in the published package, so a generated app fails with CS0234/CS0246 in the module Application project (6 errors in the probe; the API project was not reached). An earlier check that said they were present read the global NuGet cache, which holds a locally packed 1.5.0 (428 KB) and is not the published package. Fix by publishing a Platform release that contains them and pinning the CLI to it (publishing needs an explicit instruction), or by removing the scope and dimension checks from the two handler templates (a security feature, so a decision for the owner). Do not tick Step 5 until a generated app builds from nuget.org.
- [x] **Found 2026-10-23, reverted (Step 5 blocker):** the navigation wiring in the API templates (`cli/Templates/app/api/FeatureServicesSetup.sbn` `using Modulus.Navigation;` and `AddModulusNavigation()`, `FeatureEndpoints.sbn` `MapModulusNavigation()`) does not compile against the published `Synvro.Modulus.Platform` 1.5.0: its DLL has no `Modulus.Navigation` namespace. A `webapp+api` app generated today with the default packages fails with CS0234 in `Shop.Api`. It builds with 0 warnings against the local `1.5.0-local.4` feed (`feed4`), which has the namespace. Restored 2026-10-23 for the 1.6.0 release, which contains navigation: generated OpenIddict APIs wire the menu again. Generated apps must use 1.6.0.
- [x] Mongo, EF Core and RLS integration tests run for this wrap-up (2026-10-23, Podman containers, `Category=Integration`):
  `Modulus.EFCore.Integration.Tests` 46/46 (net8.0) and 49/49 (net10.0), including PostgreSQL and SQL Server row-level security,
  MySQL database-per-tenant and tenant isolation; `Modulus.Data.MongoDB.Tests` 6/6 on both targets. Zero skipped.
- [ ] Tick every box above and set the status to "complete".

### Verification

- `dotnet build modulus.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet format modulus.slnx --verify-no-changes`: clean.
- `dotnet test modulus.slnx --filter "Category=Unit"`: green. Also run the net8 leg on the touched projects (`-p:ModulusTestTfm=net8.0`).
- `dotnet list modulus.slnx package --vulnerable --include-transitive`: none.
- Integration tests (Docker or Podman) for Mongo, the outbox and RLS.
- `dotnet pack modulus.slnx -c Release`, then generate an `api --auth openiddict --multi-tenancy` app from the local feed and run its tests, including `SecurityProbeTests`.

## Keyset (seek) paging and data access (2026-10-22)

**Paging modes.** `PagingMode` (Core) names the three ways a list can be paged; the caller picks one per request:
- **Offset:** page number and size with a total count (`PagedList<T>`, `ListPagedAsync`, unchanged). Deep pages get slower and rows shift under concurrent inserts.
- **Keyset:** the caller sends the key of the last item it has seen (`after`). `IReadRepository<T>.ListKeysetAsync(spec, keySelector, selector, after, size)` returns `KeysetPage<TResult>` with `HasNextPage` and no count query. It fetches one extra row to learn whether a next page exists.
- **Cursor:** keyset with an opaque token. `CursorCodec` (Data.Abstractions) encodes the last key as base64url JSON and decodes it, and a malformed token is an `ArgumentException`. It sits in Data.Abstractions because Core is AOT-compatible and JSON serialization is not.

**Limits (deliberate).** The key must be unique and orderable in SQL: `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `double`, `float`, `decimal`, `DateTime`, `DateTimeOffset` or `TimeSpan`. A `Guid` key is refused with `NotSupportedException`, because the provider cannot order by `>` on a Guid. A specification that carries `Skip` or `Take` is refused with `ArgumentException`, because the key is the position. The order is the key, so `OrderBy` on the spec is replaced. The key and the selector run on materialized entities, since one query cannot project both the key and the selector; the seek and the ordering run on the server. Mongo does not implement keyset yet (the default interface method returns `NotSupportedException`).

**Tenancy.** Keyset adds no tenant logic, so isolation rests on the existing layers:
- **Shared database:** the tenant query filter applies inside `ApplySpec`, so a page only sees the current company's rows. `SharedDatabaseKeysetTests` checks that a seek position taken in company A pulls in none of company B's rows, and that company B's page holds only its own rows.
- **Database per tenant:** the repository resolves the tenant's own context, so the same call runs against that tenant's database and its key only needs to be unique inside it.
- **Cursor authority:** a cursor is not signed and carries no tenant. Using company A's cursor under company B returns B's rows after that key, not an error and not A's data. Clients should keep cursors per company. If a stronger guarantee is needed, bind the tenant id into the token and verify it on decode (not built).

**Data access by use case** (framework supports all three; the app chooses per query):
- **Writes (add, update, delete):** EF Core through the aggregate, so change tracking, domain events, the outbox and the tenant write guard apply. Dapper and raw SQL do not write through these paths.
- **Read models and reporting:** Dapper through `OpenTenantConnectionAsync`, which opens the context's connection with the tenant's RLS session set. Dapper is not a framework dependency; the app adds it.
- **Entity queries that need the EF model:** LINQ through the specification, or `FromSql` with parameters. Raw SQL on a tenant table inside a tenant is refused by `TenantSqlGuardInterceptor` unless the code runs in `CrossTenantSql.Allow(reason)`, so raw SQL must keep its tenant predicate.
- **Not built:** a per-query selector API that the framework chooses for the caller; the choice is made in code today. A typed query-description surface for AI tools (cursor paging plus typed specs) is the planned direction.

**Tests:** `KeysetPagingTests` (7: paging to the end, cursor round-trip, filter before window, Skip/Take refused, Guid key refused on SQLite, malformed cursor, cursor of another type) and `SharedDatabaseKeysetTests` (2: one company's page, cross-company seek). Both targets.

### Guid v7 keys (.NET 10, PostgreSQL only)

- **Generation.** `ModuleDbContext` gives a Guid key the application does not set a `GuidV7ValueGenerator` (time-ordered Guid v7), but only on a .NET 10 build with the Npgsql provider. Other providers and targets keep their own generation. Test: `A_key_the_application_does_not_set_is_generated_as_guid_v7_on_postgres` (PostgreSQL, through Testcontainers).
- **Keyset.** A Guid key is accepted by `ListKeysetAsync` only on .NET 10 with Npgsql. The seek is `key.CompareTo(after) > 0`, because a Guid has no `>` in expression trees. PostgreSQL orders `uuid` bytewise in RFC order, so the pages follow creation time. Tests: `GuidV7KeysetPostgresTests` (25 rows paged in uuid order; cursor decodes to the last id).
- **SQL Server.** Guid keys are refused for keyset and cursor paging: SQL Server orders `uniqueidentifier` by a byte order that is not chronological for Guid v7, so a keyset seek would skip or repeat rows. Use an integer or timestamp key there, with the Guid as a tie-breaker.
- **Other targets.** On .NET 8 a Guid keyset is refused with a message that names the .NET 10 requirement.

### CLI: `modulus app --framework`

- `--framework net8.0|net10.0` (default `net10.0`) sets the target of the generated app. The app's `Directory.Build.props` carries it, and every project file uses `{{ target_framework }}`, so modules, tests and the shared kernel follow it. Add-commands (`add-module`, `generate-crud`, `add-audit-store`, `add-webhooks`, `add-bff`) read the target from the app's props.
- **Package versions follow the target.** `DbProviderInfo.Version(provider, framework)` returns the same provider versions as the framework's `Directory.Packages.props` (net8.0: SqlServer 8.0.31, PostgreSQL 8.0.11, MySQL 8.0.8, SQLite 8.0.31; net10.0: 10.0.9, 10.0.2, 10.0.7, 10.0.9). EF Core, EF Design and the test packages (coverlet, System.Security.Cryptography) take their versions from `AppFramework` in the same way.
- **OpenAPI is net10-only.** The framework's OpenAPI support (`AddModulusOpenApi`, `MapOpenApi`, the BFF `AddOpenApi`) is compiled out on net8.0, so the templates emit it only for net10.0. On net8.0 the OpenAPI setup method stays as a no-op.
- **Verified.** A generated `--framework net8.0 --database PostgreSQL` app builds with 0 warnings and 0 errors and passes its 7 generated tests. A default net10.0 PostgreSQL app builds with 0 warnings and 0 errors. The CLI suite passes 575/575 (also three runs in a row after a test fix: `ModuleDiscoveryTests` now gives each test its own temp folder, because it scans every file beside the one it is given, which raced with the other tests' temp folders).
- **Engine fix.** The MySQL provider's options method is `UseMySQL` (capital SQL); the CLI emitted `UseMySql`, which did not compile. Fixed, with a regression test in `FrameworkTargetTests`. Generated SQL Server, MySQL and SQLite apps build on both targets.

### CLI: generated keyset (cursor) endpoints

- **Where.** A generated module gets a cursor query, handler and endpoint (`GET /api/{module}/{route}/cursor?cursor=&pageSize=`) and a keyset method on its repository only when `ModuleModel.CursorPaging` is true: PostgreSQL on .NET 10. Everywhere else the module keeps the offset endpoint alone. `generate-crud` reads the module's engine from the app inventory, so an existing PostgreSQL module gets the cursor files for each new entity.
- **Response.** The page is the framework's `KeysetPage<T>` inside the usual envelope: `items`, `hasNextPage`, `nextCursor`. The cursor is an opaque token; a malformed one is `400`.
- **Verified end to end.** A generated `--database PostgreSQL` app on .NET 10 was built, run against PostgreSQL 16 (Podman), and walked page by page with `pageSize=3`: seven products came back once each, in creation order, over three pages, and a malformed cursor returned `400`. Gate-off apps (net8.0 PostgreSQL, SQLite net8.0) build with 0 errors. Template tests: `CursorPagingTemplateTests`.
- **Packaging note.** A local feed has to be repacked after framework changes (`dotnet pack -o <feed>`); the generated app otherwise restores the old `Synvro.Modulus.*` packages, which lack `KeysetPage`.

<!-- source: docs/SECURITY_STAGE2_PLAN.md -->
## Security stage 2: Company = Tenant

Roadmap for the security foundation required by the ERP Framework Guideline v2 (Part A invariants, Build
Order stage 2): company isolation, explicit membership, a resolved policy for every endpoint, tamper-evident
audit and CI-blocking isolation tests.

Status (2026-10-03, follow-up session): **Phases 1 to 5 are built in the framework** and the integration tests ran
against real databases (Docker), and `modulus app --multi-tenancy` wires them into generated apps (1.8, the
per-entity isolation test of 5), except the UI items (Files UI tenant roots, the Security tab of
`Modulus.UI.AuditLogging`), which were deferred by request. Log redaction is built too (see "Log redaction"). The samples (`samples/TradeFlow`)
are not changed by this work. Checkboxes are updated as work lands; where the build differs from the plan, the
original text is kept and the difference is noted inline as **As built:** or **Deviation:**.

### Decisions

- **Company = Modulus tenant** (`ICurrentTenant`, `TenantInfo`). Company isolation is tenant isolation.
- **Branch / Location = org units** (`IOrgHierarchy`, `OrgPlacement`, `IHasOrgUnit`), which are already
  tenant-scoped. Inheritance down the hierarchy happens only through an explicit placement mode
  (`UnitOnly` / `UnitAndDescendants` / `UnitAndAncestors`), never implicitly.
- **Group = a set of tenants** (`TenantInfo.GroupId`). Group-level views go through a federated read path
  (later stage), never a cross-tenant query.
- **Multi-company access: one login + server-side membership check** (guideline B1 option 1). The selected
  company (header, subdomain, switcher) is allowed only when a membership row exists, so revoking a membership
  takes effect immediately. A `tid` claim still pins a token to one company (audience-bound / AI tokens).
- Network trust (B4) and AI / MCP (Part D) are stages 4 and 5; `ISecurityContext` reserves their slots.

### Context (gaps found before this work)

- **Membership.** `ModulusUser.TenantId` holds a single tenant and there is no membership concept. Host accounts
  (no `tid`) can enter any tenant by header unless `RequireHostTenantAccessPolicy` is set (`TenantMiddleware`).
- **Messages and jobs.** Every message/job path trusts the tenant id: `new TenantInfo(id, "")` with no store or
  IsActive check. The paths are `OutboxProcessor`, `EnvelopeAmbientScope`, Sagas `AmbientContextIncomingStep`,
  `ChannelJobQueue` and `QuartzJobAdapter`. In the inbox, `InboxMessage.TenantId` is never set.
- **Writes.** `ModuleDbContext` stamps a tenant on Added entities but accepts an explicit foreign `TenantId`.
- **Endpoints.** No fallback authorization policy is configured. Only REPR endpoints default to authenticated;
  minimal-API `IEndpoint`s have no default, and nothing enumerates `EndpointDataSource`.
- **Audit.** Only `InMemoryAuditLogStore` exists. There is no hash chain, and business and security audit are
  not separated.
- **Storage.** Storage paths are caller-supplied with no tenant prefix (Local, S3, Azure Blobs).
- **Database.** There is no database-enforced isolation on any provider: no RLS, no session context, no raw-SQL
  guard, and MongoDB relies on each repository remembering `MongoTenantFilter`.
- **Tests.** `TestAuthHandler` never emits `tid`, so test principals act as host accounts.

Every phase leaves `dotnet build` at 0 warnings, `Category=Unit` green and `dotnet format` clean, updates
`PublicAPI.Unshipped.txt` and AGENTS.md, and finishes before the next phase starts.

### Phase 1: Tenancy foundation

#### 1.1 Tenant membership
- [x] **`ITenantMembershipStore`** (Platform `MultiTenancy/`) with two methods:
  - `IsMemberAsync(userId, tenantId)`
  - `ListTenantIdsAsync(userId)`
- [x] **Implementations:**
  - In-memory default (`InMemoryTenantMembershipStore`, seed with `Add`). It starts empty, so `RequireMembership()` without a real store fails closed.
  - `EfTenantMembershipStore` (`Modulus.MultiTenancy.EntityFrameworkCore`, table `ModulusTenantMemberships`, PK (UserId, TenantId), `IsActive`, `CreatedAtUtc`). It counts only active memberships of active tenants. `TenantManager.AddMemberAsync` / `RemoveMemberAsync` grant and revoke; revoking sets the row inactive, and re-adding re-activates it.
- [x] **`TenantMiddleware`:** when `RequireMembership()` is on, an authenticated caller without `tid` who selects a tenant must be a member, else `403`.
  - `HostTenantAccessPolicy` stays as the break-glass override (logged as a warning; Phase 4 audits it).
  - A `tid` claim keeps pinning the token.
  - A caller with no user id claim (`NameIdentifier` / `sub`) is refused.
- [x] **CLI templates** turn `RequireMembership()` on; the library default stays off for one release. Done in 1.8 (`modulus app --multi-tenancy`).

#### 1.2 Group
- [x] `TenantInfo.GroupId` (optional, defaulted) and `TenantEntity.GroupId` (indexed).
- [x] `ITenantStore.ListByGroupAsync(groupId)` (default interface method; `EfTenantStore` queries the index).

#### 1.3 `ISecurityContext`
- [x] **Core abstraction:** `Kind`, `User`, `CompanyId`, `GroupId`, `BranchId`, `Network` (always `Unverified` for now), `Agent` (always null for now), `CorrelationId`.
  - It composes `ICurrentTenant`, `ICurrentUser`, `ICurrentDataScope` and `ICorrelationContext`.
  - `Kind` is `System` when there is no HTTP request and no authenticated user (jobs, consumers).
- [x] **Branch selection:** optional `X-Branch-Id` (`BranchContextMiddleware`), accepted only when the caller's org scope includes the unit, else `403`. Repeated or malformed headers and anonymous callers are refused too.
- [x] **Registration:** `AddModulusSecurityContext()` + `UseModulusSecurityContext()` (after authentication) in Platform; `NullSecurityContext` is the Core default.

#### 1.4 Verified tenant restore for messages and jobs
- [x] **Seam:** `ITenantContextRestorer.VerifyAsync(tenantId)` (Core) returns the verified `TenantInfo`. `AddMultiTenancy` registers `VerifiedTenantContextRestorer`, which resolves the id through `ITenantStore` (30 s cache) and throws `TenantContextRejectedException` for unknown or inactive tenants. Without multi-tenancy the id is applied unverified.
  - **As-built lesson:** verifying and entering are two steps. `sp.EnterTenant(await sp.VerifyTenantAsync(id, ct))` enters the tenant in the frame that runs the work. A first version called `ICurrentTenant.Change` inside the async restorer, and since an `AsyncLocal` written inside an `async` method does not flow back to the caller, the tenant was never ambient. The tests now use a real `AsyncLocal` tenant to catch this.
- [x] **Call sites switched over:**
  - `OutboxProcessor` and `MongoOutboxProcessor`: a rejected tenant dead-letters the row at once (`RetryCount = MaxRetries`).
  - RabbitMQ / Kafka consumers through `EnvelopeAmbientScope.VerifyTenantAsync` + `Restore`: RabbitMQ nacks without requeue, Kafka routes to the DLQ with reason `tenant-rejected`.
  - Sagas (`AmbientContextIncomingStep`): the message ends in the error queue.
  - `ChannelJobQueue` and Quartz: the job is dropped (counted as failed and logged).
- [x] **Inbox:** `EfInboxStore` and `MongoInboxStore` stamp `InboxMessage.TenantId` from the ambient tenant.

#### 1.5 Write-side guard
- [x] `ModuleDbContext.SaveChangesAsync` throws `CrossTenantWriteException` (Core; mapped to `403` by `GlobalExceptionHandler`) outside the host context when:
  - an Added `IHasTenantId` is stamped with another tenant (an unstamped one is still stamped with the current tenant)
  - a tracked row's `TenantId` changed
  - a Modified or Deleted row carries another tenant's id (for example an attached stub)
  - no tenant is resolved and the row carries any tenant id (only tenant-less rows may be written)

  Limit: an attached stub that lies about its tenant id still reaches the row by primary key. Closing that is the database's job (Phase 3 RLS / per-tenant databases).

#### 1.6 Tenant-scoped storage
- [x] `TenantScopedFileStorage` decorator prefixes `tenants/{id:N}/` (host: `host/`), behind `StorageOptions.IsolateTenants` (`Storage:IsolateTenants`, library default off).
  - Rooted paths and `.` / `..` segments are rejected (the local provider canonicalizes `..`, which would otherwise reach a sibling tenant's folder).
  - No tenant resolved means every call throws (fail-closed).
  - Every provider registers through `UseFileStorageProvider<T>()` (local, S3, Azure Blobs), which wraps at resolution time, so the order of `Configure` and provider registration does not matter.
  - Files UI: still path-addressed, but inside the tenant's prefix once isolation is on.

#### 1.7 Testing harness
- [x] `TestAuthHandler`: an `X-Test-TenantId` header becomes the `tid` claim (`TestAuthDefaults.TenantIdHeader`).
- [x] `CreateAuthenticatedClient(..., tenantId, pinTenant: true)` option. A membership is seeded through the app's own store (`InMemoryTenantMembershipStore.Add` or `TenantManager.AddMemberAsync`), so `Modulus.Testing` takes no dependency on `Modulus.Platform`. This means there is no `SeedMembershipAsync` helper.

#### 1.8 CLI templates
- [x] Program template wires `RequireMembership()`, `AddModulusSecurityContext()` and `IsolateTenants`.
- [x] Identity seeding adds the seeded admin's membership.

  **As built:** behind `modulus app --multi-tenancy` (design below). The membership is added by the tenancy module's
  `SeedTenancyAsync` (every `Admin` user, in the default company), not by Identity seeding, so an app without the option
  is unchanged.

  **Migration note:** existing EF tenant-store databases need the new `ModulusTenantMemberships` table and the `ModulusTenants.GroupId` column. The tenant store ships no migrations, so an app that owns that schema adds them.

### Phase 2: Endpoint policy model and startup guard

- [x] **Endpoint security metadata** (Core, `Modulus.Core.Abstractions.Security`, no ASP.NET dependency, so every package can use it):
  - `LoosenedAttribute(reason) { Ticket, Framework }`: why an endpoint is anonymous. It works both as endpoint metadata and as an attribute next to `[AllowAnonymous]`; the attribute alone opens nothing.
  - `EndpointSecurityPolicyAttribute { Classification, Network }` with `DataClassification` (Unspecified/Public/Internal/Confidential/Restricted) and `NetworkRequirement` (Any/CompanyNetwork/BranchNetwork/Internal).
  - Permissions are not duplicated: they stay the endpoint's authorization data.
  - **Deviation:** `RequiresApproval` is not built. The approval workflow (`Modulus.Authorization.Workflow`) now exists, but it is
    driven by code (`IApprovalWorkflow.SubmitAsync`), not by an endpoint flag. A declared flag that nothing enforces is still worse than none.
- [x] **Fallback policy** = authenticated user. `AddModulusAuthorization(o => o.RequireAuthenticatedUserByDefault)` (default `true`) sets it unless the app set its own.
  - It also covers requests that match no endpoint, so unknown routes answer `401` to anonymous callers, and so do static files served after `UseAuthorization`. The CLI's UI wiring therefore puts `UseStaticFiles()` before `UseAuthentication()`.
- [x] **`.Loosen(reason, ticket)`** (`Modulus.AspNetCore.Security.Policy.EndpointSecurityExtensions`, plus a `Loosen(LoosenedAttribute)` overload) and REPR `AllowAnonymous(reason, ticket)` / `Classification(...)`. `.WithSecurityPolicy(classification, network)` covers minimal APIs.
  - **Deviation:** the bare REPR `AllowAnonymous()` is not `[Obsolete]`: under `TreatWarningsAsErrors` that would break every app at upgrade. MOD0001 flags it at build time, and the guard refuses it at boot.
- [x] **Every framework anonymous endpoint carries a reason** (`Framework = true`): health live/ready, `/health/modules`, `/health/graph`, gRPC health/reflection, GraphiQL, BFF login/logout, `/_error/{code}`, the Identity UI's Login/Register/LoggedOut/AccessDenied pages, the app's own `/Account` folder when it calls `AllowAnonymousToFolder("/Account")` (other folders count as app loosenings), and the OpenIddict token/authorize/introspect/end-session endpoints plus the account controller's anonymous actions. `/connect/userinfo` gained `[Authorize]`.
  - **Not loosened on purpose:** GraphQL and Realtime with `RequireAuthenticatedUser: false`. Endpoint-level anonymity overrides the policy of an enclosing route group (a BFF client's), so an app that wants them anonymous calls `.Loosen(...)` on the returned builder.
  - **Changed in phase 5:** `/_ui/menu` was on this list, which made the guard refuse to start the Web host of a `webapp+api` app (no fallback policy there, so the menu was `unpoliced`); the phase 5 end-to-end run found it. The menu endpoint came from the server-rendered UI that was removed from this repository (`src/ui`). No code here maps `/_ui/menu` now, so this entry describes the removed endpoint's behaviour, not a current loosening.
- [x] **Startup guard** `AddModulusSecurityGuard(configuration)` (settings `Security:Guard`: `Enabled`, `AllowListPath` = `security/loosening-allowlist.json`, `FailOnUnlistedInDevelopment`).
  - It runs in `IHostedLifecycleService.StartedAsync`: the web host builds its pipeline (which hands the mapped endpoints to routing) after the other hosted services' `StartAsync`. Throwing there still fails `Host.StartAsync`.
  - The pure `EndpointSecurityAnalyzer` resolves each endpoint to `Policed` / `Fallback` / `Loosened` / `Unpoliced`.
  - Errors in every environment: `unpoliced`, `anonymous-without-reason`, `anonymous-classified-data` (Confidential/Restricted), `network-not-enforced`.
  - `loosening-not-allow-listed` fails outside Development and warns in Development. Framework loosenings are exempt from the allow-list (still reported); the allow-list governs the app's own decisions.
  - An allow-list entry's reason also explains a third-party bare `AllowAnonymous` (for example a YARP route with `RequireAuthentication: false`).
  - Static-asset endpoints are skipped.
  - The loosening report is logged and kept in `SecurityGuardState.Report` (for Phase 4's audit).
- [x] GraphQL fields, realtime topics, jobs and consumers in the report.
  - **As built (2026-10-03, follow-up):** `ISecuritySurfaceContributor` (Core, `Modulus.Core.Abstractions.Security`) lets a package describe the items one endpoint carries; the guard puts them in `SecurityGuardReport.Surfaces` (`SecuritySurfaceEntry`: surface, name, `Policed` / `Inherited` / `Anonymous`, policies), logs the anonymous ones and adds them to the loosening fingerprint it records in the audit. `Modulus.GraphQL` lists every root field (`Query.*`, `Mutation.*`; a field with `AuthorizeWithPolicy` / roles / `Authorize` is policed, any other inherits the endpoint's sign-in, or is anonymous with `RequireAuthenticatedUser: false`), `Modulus.Realtime` every topic (permission or callback = policed).
  - **Deviation:** surfaces are reported, not judged (no finding fails startup: each item sits behind an endpoint the guard already checked). Nested GraphQL fields are not listed (they are reached through a root field), nor are pushed realtime events (their audience is computed per event, always inside the publishing tenant). Jobs and consumers have no caller to authorize; their control is the verified tenant restore (1.4).
- [x] **Analyzer `MOD0001`** (`src/analyzers/Modulus.Analyzers`, shipped as `analyzers/dotnet/cs` inside `Synvro.Modulus.AspNetCore`, Warning). It flags anonymous without a reason: a `.AllowAnonymous()` chain without `LoosenedAttribute` metadata, `[AllowAnonymous]` without `[Loosened]`, and the parameterless REPR `AllowAnonymous()`. It is active only where `LoosenedAttribute` resolves.
  - **Deviation:** the planned rule ("raw `Map*` outside `IEndpoint`") would flag every `Program.cs`. With the fallback policy a raw endpoint is closed rather than open, so the analyzer targets the guard's hard error instead.
- [x] **CLI.** Hosts with a sign-in (not `--auth none`, not a web app on an external provider, whose pages have no cookie sign-in yet) call `AddModulusSecurityGuard` and ship `security/loosening-allowlist.json`.
  - API host: lists `/openapi/{documentName}.json`, loosened in Development. Without the example permission it calls `AddModulusAuthorization()` for the fallback policy.
  - Web host of `webapp+api`: lists `/health/live`.
  - **Samples untouched:** an earlier draft gave the TradeFlow sample's anonymous REPR endpoints reasons; that change was reverted (framework-only scope). The sample still uses the bare `AllowAnonymous()`, which MOD0001 flags and the guard refuses if the sample ever calls `AddModulusSecurityGuard`.

**Verified** (packed 1.4.0 to a scratch feed, apps restored with an isolated package cache):
- `modulus app --auth openiddict` for `api` (6/6 tests), `web` + `generate-crud` (3/3) and `webapp+api` + `generate-crud` (9/9). All build with 0 warnings.
- API host log: "20 endpoints checked, 12 anonymous, 0 findings". Anonymous `GET /api/v1/products` and an unknown route both answer `401`.
- Adding `app.MapGet("/raw", ...).AllowAnonymous()` fails boot with `[anonymous-without-reason]`, and MOD0001 reports it at build. `.Loosen("...")` without an allow-list entry fails boot (Testing environment) with `[loosening-not-allow-listed]`.
- Web app in Development: the login page and its theme CSS/JS answer `200` to an anonymous visitor.

**Found on the way, fixed in the follow-up:** the generic `AccountController<TUser>` used `[Route("[controller]")]`, so its endpoints were mapped under the generic type name (`AccountController`1/forgot-password`). It is now `[Route("account")]` (`/account/forgot-password`, `/account/logout`, ...), pinned by a route test.

### Phase 3: Data isolation for every supported database

Requirement: company isolation must hold on **every** provider Modulus ships (PostgreSQL, SQL Server, MySQL,
SQLite, MongoDB), not only PostgreSQL. Each provider gets the strongest database-enforced control it supports.
Where the engine has no row-level security, the gap is closed by a database boundary and a framework guard.
The EF global filter and the write guard from 1.5 stay in front on every relational provider.

| Provider | Shared database (Tier S) | Database per tenant (Tier M/L) |
|---|---|---|
| PostgreSQL | Native RLS: `set_config` per connection, plus `ENABLE` + `FORCE` RLS policy `modulus_tenant` | `AddModuleDatabasePerTenant` / `TenantInfo.ConnectionString` |
| SQL Server | Native RLS: `sp_set_session_context` per connection, plus inline predicate function and `SECURITY POLICY` (filter + block predicates) | `AddModuleDatabasePerTenant` / `TenantInfo.ConnectionString` |
| MySQL | No RLS. The raw-SQL guard rejects unfiltered and raw SQL on tenant tables. Tier S is supported only with that guard; database per tenant is recommended | `AddMySQLPerTenantDatabase` |
| SQLite | No RLS, single file. **One file per tenant** is the boundary; a shared file is development-only (the guard still applies) | `AddSQLitePerTenantDatabase` (`tenants/{id:N}.db`) |
| MongoDB | `TenantScopedCollection<T>`: every read, update, delete, aggregate and bulk write gets the tenant predicate, inserts are stamped and validated | `AddMongoDatabasePerTenant` (`ITenantMongoDatabase`), tested against real MongoDB |

#### Shared
- [x] **Session-context seam.** **As built:** an abstract `TenantSessionInterceptor` (`Modulus.EntityFrameworkCore.Isolation`; providers implement `Supports(DbConnection)` and `Configure(DbCommand, TenantSession)`) declared per context with `AddTenantSessionContext<TContext>(interceptor)`, instead of an `ITenantSessionContext` service.
  - It writes the session on `ConnectionOpened` and remembers what each connection carries (`ConditionalWeakTable`). Before every reader, scalar and non-query command it rewrites the session when the ambient tenant changed since, so a connection held open across a tenant switch follows the switch, and a pooled connection never keeps the previous tenant.
  - It forgets the connection on close and after a rollback (a rolled-back transaction also rolls back the `set_config` on PostgreSQL).
  - It acts only for `ModuleDbContext` contexts and only for contexts that declared it.
- [x] **Raw-SQL guard** (`TenantSqlGuardInterceptor`, added by `AddModuleDatabase` to every module context, every relational provider).
  - Rejected with `CrossTenantSqlException` (`Tables`, `Origin`): raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) and `IgnoreQueryFilters()` queries that reference a table of an `IHasTenantId` entity, inside a tenant or with no tenant. The host passes.
  - `IgnoreQueryFilters()` and composed raw-SQL roots are tagged when EF compiles the query (`modulus:unfiltered`, `modulus:raw-sql`), so the decision is taken per execution against the tenant in scope, even though the compiled query is cached.
  - **Deviation:** the opt-in is an ambient scope, `using (CrossTenantSql.Allow(reason)) { ... }`, not a `.AllowCrossTenantSql(reason)` call-site extension (it has to cover `ExecuteSql` and `SqlQuery` too). Both the opt-in and every rejection are security-audited (phase 4).
  - Limit: table references are found by name in the command text, so a statement reaching a tenant table through a view or function is not detected. RLS covers those on PostgreSQL and SQL Server.
  - The testing harness (`Modulus.Testing` SQLite swap) re-adds the guard, so app tests run with it.
- [x] **Dapper / read models.** **As built (follow-up):** `await using var lease = await db.OpenTenantConnectionAsync(ct)` (`TenantConnectionExtensions`, returns a `TenantConnection` with `Connection` and the context's current `Transaction`). With row-level security the session is written before the connection is returned, even when it was already open for another tenant; database per tenant needs nothing more; on a database shared **without** RLS (filter only: MySQL, SQLite, an undeclared PostgreSQL/SQL Server context) it throws `CrossTenantSqlException` inside a tenant or with no tenant unless a `CrossTenantSql.Allow(reason)` scope is open (host allowed); refusals and opt-ins are audited like the guard's. Disposing the lease releases the connection (reference-counted with EF's own open). Limit: re-open after a tenant switch; commands run on the lease are not seen by the raw-SQL guard.
- [x] **Tier check at startup.** `AddModulusDataIsolationCheck(configuration)` reads `Security:DataIsolation:Tier`:
  - unset: nothing is checked;
  - `DatabasePerTenant`: every tenant-aware context must be registered per tenant, otherwise startup fails;
  - `Shared`: a shared SQLite file fails outside Development, and a context without row-level security (filter only) logs the residual risk as a warning.
- [x] **Database per tenant (any relational provider).** `AddModuleDatabasePerTenant<TContext>(hostConnectionString, tenantConnectionString, configure)`: the host scope uses the host database and a tenant uses its own. With no tenant in scope, resolving the context throws ("no tenant is in scope") instead of falling back to a shared database.

#### PostgreSQL
- [x] **Deviation:** `services.AddPostgreSqlRowLevelSecurity<TContext>()` next to `AddPostgreSQLDatabase`, not a `UseTenantRowLevelSecurity()` option on it. It runs `SELECT set_config('modulus.tenant_id', @tenant, false), set_config('modulus.is_host', @host, false)`.
- [x] Migration helpers `migrationBuilder.EnableTenantRowLevelSecurity(table, tenantColumn = "tenant_id", schema)` / `DisableTenantRowLevelSecurity`: `ENABLE` + `FORCE` RLS and the `modulus_tenant` policy (`USING` and `WITH CHECK`: the tenant column equals the session tenant, or the session is the host).
  - Also `PostgreSqlRowLevelSecurity.Script(model)` (every tenant table of a model) and `EnsureAsync(context)` for apps on `EnsureCreated` or for the migrator job.

#### SQL Server
- [x] **Deviation:** `services.AddSqlServerRowLevelSecurity<TContext>()` (an `EXEC sp_set_session_context` of `modulus.tenant_id` as `uniqueidentifier` and `modulus.is_host`), not an option on `AddSqlServerDatabase`.
- [x] Migration helpers `EnableTenantRowLevelSecurity(table, tenantColumn = "TenantId", schema = "dbo")` / `DisableTenantRowLevelSecurity`. They create schema `modulus` and the inline TVF `[modulus].[fn_tenant_predicate]` when missing, then one policy per table, `[modulus].[tenant_{schema}_{table}]`, with a `FILTER` predicate and `BLOCK` predicates (AFTER INSERT, AFTER UPDATE). `SqlServerRowLevelSecurity.Statements(model)` and `EnsureAsync(context)` too.

#### MySQL and SQLite
- [x] Raw-SQL guard on (shared, above).
- [x] `AddMySQLPerTenantDatabase<TContext>(hostConnectionString, tenantConnectionString, configure?)`.
- [x] `AddSQLitePerTenantDatabase<TContext>(directory = "./tenants")`: `{directory}/{tenant:N}.db`, host `{directory}/host.db`.

#### MongoDB
- [x] **`TenantScopedCollection<T>`** (`Modulus.Data.MongoDB`) wraps `IMongoCollection<T>`:
  - reads and deletes (`Find`, `CountDocuments`, `DeleteOne/Many`, `FindOneAndDelete`) are scoped;
  - `InsertOne/Many` and `ReplaceOne` stamp the tenant and reject a foreign one (`CrossTenantWriteException`); with no tenant resolved an insert is refused;
  - updates (`UpdateOne/Many`, `FindOneAndUpdate`) are scoped and may not touch the tenant field (`$set` / `$unset` / `$rename`, or `$replaceRoot` / `$replaceWith` in a pipeline), except in the host scope;
  - `Aggregate` prepends a `$match`, and `BulkWrite` rewrites every model; an upsert inherits the tenant from the scoped filter;
  - with no tenant nothing matches; the host sees everything.
- [x] `ModuleMongoContext` gains a constructor taking `ICurrentTenant` and `GetTenantCollection<T>(name)`; `MongoRepository` stamps on add and update.
  - **Deviation:** `GetCollection<T>` (the raw collection) is kept, with a documentation warning; removing it would break every existing Mongo module. `TenantScopedCollection.Unscoped(reason)` is the explicit opt-out.
  - **As built (follow-up):** `Unscoped(reason)` is audited (`mongo.unscoped`, `overridden`, collection and reason) when the collection has an `ISecurityAuditLog` (new optional constructor argument; `ModuleMongoContext` passes the one it was given).
- [x] Database-per-tenant resolver for Mongo. **As built (follow-up):** `AddMongoDatabasePerTenant(o => ..., id => $"shop_{id:N}")` registers a scoped `ITenantMongoDatabase` (the tenant's database; the host database in the host context; no tenant throws, and a tenant database may not be the host's). `AddMongoDatabase` registers the shared variant, so a module whose context takes `ITenantMongoDatabase` (new `ModuleMongoContext` constructor) runs in either tier. `IMongoDatabase` stays the host database for infrastructure (outbox, inbox, health). Tested against a real MongoDB (`TenantMongoDatabaseIntegrationTests`).

#### Roles and docs
- [x] [`docs/security/database-roles.md`](#database-roles-for-tenant-isolation): a migration role that owns the schema and an application role subject to RLS, with scripts for PostgreSQL, SQL Server and MySQL, plus SQLite notes.
- [x] AGENTS.md: provider × tier × enforcement table.

#### Tests
- [x] **Unit:** `TenantDataIsolationTests` (`Modulus.EntityFrameworkCore.Tests`):
  - the guard rejects raw queries, raw commands and `IgnoreQueryFilters()` per execution, and the opt-in lets them through;
  - the session interceptor is applied on open, refreshed on a tenant switch, forgotten after a rollback, and only on declared contexts;
  - per-tenant databases and SQLite file names;
  - the tier check matrix;
  - the PostgreSQL and SQL Server scripts.

  `TenantScopedCollectionTests` (new `Modulus.Data.MongoDB.Tests`, 10).
- [x] **Integration (Testcontainers), written and compiling, not run on the build machine (no Docker there):**
  - PostgreSQL: the app connects as a non-owner `modulus_app` role. `IgnoreQueryFilters()` and `FromSql` in tenant B still see only B's rows. A raw cross-tenant `UPDATE` fails with `42501`. With no tenant nothing is visible, and the host sees everything.
  - SQL Server: the same, plus the block predicate and a tenant switch on one open connection.
  - MySQL: database per tenant.
  - MongoDB: wrapped find, update, delete, aggregate and bulk calls plus an upsert.

  **Run them** with `dotnet test --filter Category=Integration` on a machine with Docker before relying on them.

### Phase 3b: Isolation of the other data layers (guideline A6)

An audit of every non-database store found the following:

| Layer | Before this work | Status now |
|---|---|---|
| Cache (memory, Redis, FusionCache, `[CacheFor]`) | `CacheKeys.Entry/Tag` → `modulus:entry:{tenant:N}:…` in every service and the mediator behaviours | OK (no change) |
| **BFF composer cache** | `bff:section:{client}:{key}:{sub}`; one user in two companies got company A's section in company B | **Fixed:** the selected company is part of the key |
| **File storage** | Caller-supplied paths | **Fixed in 1.6** (`TenantScopedFileStorage`); the Files UI is deferred (UI) |
| **Notifications** | `tenantId == null` meant "all tenants" in `ListAsync` / `MarkAllAsReadAsync` | **Fixed:** exact tenant match, null = tenant-less notifications only |
| Settings | Keyed by (name, tenant, user) | OK |
| SignalR hub groups | `tenant:{id}:` prefix | OK: already refused without a tenant (host included); now tested |
| Realtime, Webhooks | Per tenant | OK; restore paths go through 1.4 |
| Idempotency keys | Scoped by `ICurrentTenant` | OK |
| Outbox / inbox | Inbox never set `TenantId` | Fixed in 1.4 |
| Localization | Global texts | OK (by design) |
| **Logs** | No tenant in the log scope, no redaction | **Scope fixed**; redaction built (item 5) |
| Exports | None in the framework | Design note below |
| Search / vector | Dropped from the framework | Rule for the AI stage: namespace per tenant + permission filter at retrieval |

- [x] **BFF.** The composer's section cache key is `bff:section:{client}:{company or -}:{key}[:{sub}]` (`BffOptions.TenantHeader`, default `X-Tenant-Id`; the company is the `tid` claim, else that header). `BffTenantHeaderHandler`, added by `AddBffUserAccessToken()`, forwards the selected company to upstream calls. The upstream still checks membership: the BFF never picks a company itself. YARP routes forward the header as part of the request. Test: `Cached_sections_are_kept_apart_per_selected_company`.
- [ ] Storage: Files UI tenant-rooted paths. **Deferred (UI).** The storage layer itself is isolated by 1.6.
- [x] **Notifications.** `InMemoryNotificationStore.ListAsync` / `MarkAllAsReadAsync` match the tenant exactly; `INotificationStore` documents the rule for other implementations (`NotificationTenantIsolationTests`).
- [x] **SignalR.** `ModulusHub.JoinTenantGroupAsync` / `LeaveTenantGroupAsync` already threw without a tenant (the host scope too, which has no company). `ModulusHubTenantGroupTests` pins it.
- [x] **Log scope.** `CorrelationIdMiddleware` opens a `CorrelationId` scope, and `TenantMiddleware` opens `TenantId` + `UserId` (ids only, never names or e-mail addresses). Providers that include scopes (console with `IncludeScopes`, OpenTelemetry logs) carry them on every line.
- [x] **Redaction.** `Microsoft.Extensions.Compliance.Redaction` (MIT) redacts only data annotated with a data-classification taxonomy and logged through source-generated `[LoggerMessage]` / `[LogProperties]`, so it would not catch `[ProtectedPersonalData]` values written through ordinary log calls. It needs a classification design first (reuse `DataClassification` from phase 2). **As built:** see "Log redaction" below (taxonomy `Modulus`, `AddModulusRedaction`, analyzer MOD0002 for ordinary log calls). Framework code keeps logging ids, not personal data, and the security audit never records user names, passwords, tokens or SQL text.
- [x] **Exports (design note).** When an export feature lands it is an exfiltration channel, so:
  - each export is its own endpoint with a permission (not the list endpoint's), and it is classified (`EndpointSecurityPolicyAttribute`);
  - it reads through the tenant-filtered context only; no `CrossTenantSql.Allow`, and group-level exports go through the federated read path;
  - every export is recorded in the security audit (`data` category: who, which entity set, row count, filter summary, never the rows);
  - files are written through `TenantScopedFileStorage` with a short-lived download link.

### Phase 4: Security audit, hash-chained

- [x] **`ISecurityAuditLog`** (Core, `Modulus.Core.Abstractions`): `Record(SecurityAuditEvent)` never blocks and never throws, so synchronous sources (EF command interceptors) and `403` paths can call it. `SecurityAuditEvent` carries:
  - `Category` (`SecurityAuditCategories`: tenancy, authorization, identity, data, configuration)
  - `Action`
  - `Outcome` (`success` / `denied` / `overridden`)
  - `TenantId` (null = host chain)
  - `Actor`, `Target`, `CorrelationId`, `OccurredAt` and `Details`

  `NullSecurityAuditLog` is the default (`AddModulus` registers it).
- [x] **Chain** (`Modulus.Platform`, `Modulus.AuditLogging.Security`):
  - **One chain per company**, plus the host chain (`Guid.Empty`).
  - `SecurityAuditChain.ComputeHash` is SHA-256 over canonical JSON: keys in ordinal order (details too), no whitespace, UTC timestamps truncated to milliseconds so every database round-trips them, and the previous hash included. The first entry links to 64 zeros.
  - `Verify(records, anchor?)` checks contiguous sequences from 1, every link and every hash. It reports the first broken sequence and why: missing/reordered, unlinked, or edited.
- [x] **`AddModulusSecurityAudit(configuration)`** (settings `Security:Audit`):
  - It replaces the no-op log with `SecurityAuditLog`, a bounded in-memory queue (`QueueCapacity`, default 10 000) drained in order by the `SecurityAuditWriter` hosted service. A failed append is retried (`MaxAppendAttempts`), then reported lost at Critical. The queue is drained on shutdown.
  - The store is `ISecurityAuditStore` (in-memory by default).
  - **Deviation:** events are queued, not written in the request, so events recorded just before a crash can be lost, and a full queue drops the new event with an error log rather than blocking the request.
  - **As built (follow-up):** `Security:Audit:SpoolFile` makes the queue durable: each event is journaled (flushed to disk) before it is queued and acknowledged once stored; unacknowledged events (a crash, a full queue, a store that kept refusing) are appended at the next start, in order. At least once: an event stored in the instant before its acknowledgement comes back as a second entry. The file is truncated whenever nothing is outstanding; one file per process.
- [x] **Anchors.** `AuditAnchorService` writes every chain head to each `IAuditAnchorSink` every `AnchorInterval` (default 1 h), only when a head moved.
  - `FileAuditAnchorSink` appends JSON lines, configured through `Security:Audit:AnchorFile`; with no file set, nothing is written. Object-storage sinks are the app's own `IAuditAnchorSink` (no `IFileStorage` sink is shipped).
  - `VerifyChainAsync(tenantId, anchor)` also catches a chain recomputed from scratch after an edit, and a truncated chain.
- [x] **`Modulus.AuditLogging.EntityFrameworkCore`** (new package): `ModulusAuditDbContext` (derive the app's own context; it has no tenant filter, on purpose) and `AddModulusAuditStore<TContext>()`.
  - Business log: `EfAuditLogStore`, table `modulus_audit_logs`.
  - Security chain: `EfSecurityAuditStore`, table `modulus_security_audit` with **primary key (ChainId, Sequence)**. An append reads the head, links after it and inserts; a writer that loses the race to another replica gets a key violation, re-reads and retries. One process serializes its own appends.
  - SQLite stores `DateTimeOffset` as binary so queries can sort it.
  - `SecurityAuditDatabaseScripts.PostgreSql / SqlServer / MySql(role, ...)` make the table append-only: the app role gets `SELECT, INSERT` only (`REVOKE` / `DENY UPDATE, DELETE`), and a trigger refuses `UPDATE` / `DELETE` (and `TRUNCATE` on PostgreSQL) for every role. Identifiers are validated, never interpolated raw.
- [x] **Sources:**
  - **Tenancy:** `TenantMiddleware` records `tenant.claim-unresolved`, `tenant.claim-mismatch` and `tenant.not-a-member` (denied) and `tenant.break-glass` (overridden), in the chain of the company that was reached. `TenantManager` records `membership.added/removed` and `tenant.activated/deactivated`.
  - **Authorization:** `AuthorizationSecurityAuditHandler` turns the relayed `AuthorizationAdministrativeChangeEvent` (grants, roles, org units, entitlements, delegations) and the audited `AccessDecisionAuditEvent`s into chain entries.
    - **Deviation:** they arrive through the existing outbox relay, so they need `AddEfCoreAuthorizationAudit`; with the no-op authorization writer nothing is relayed.
  - **Identity:** `IdentityPasswordGrantValidator` records why a password sign-in was refused: unknown user (host chain), disabled, not allowed, locked out, or wrong password. `ModulusTokenController` records issued and refused `token.password`, `token.refresh`, `token.authorization-code` and `token.client-credentials`. No user names, passwords or tokens are recorded.
    - **As built (follow-up):** `/connect/revoke` is handled by OpenIddict alone, so `RevocationSecurityAudit` adds two server event handlers (registered by `AddModulusOpenIddict`): `token.revoke` success after OpenIddict's own `RevokeToken` handler (subject, company, token type, client; never the token), and denied when an error response is applied.
  - **Data:** `TenantSqlGuardInterceptor` records each rejection (denied) and each `CrossTenantSql.Allow` use (overridden, with the reason). It never records the SQL text.
  - **Configuration:** the startup guard records `startup.loosening-report` at every start in the host chain: endpoint, anonymous and finding counts, the anonymous routes, and a SHA-256 fingerprint of them, so a new loosening shows up in the audit.
- [ ] **UI:** Security tab in `Modulus.UI.AuditLogging` with chain status. **Deferred (UI).**
- [x] **Tests:**
  - `SecurityAuditChainTests` (Platform): per-company contiguous chains; an edited, removed or rewritten entry is detected (the anchor catches the rewrite and truncation); canonical hashing; 200 concurrent appends; the writer delivers in order; anchors are written only when a head moves.
  - `EfSecurityAuditStoreTests` (new `Modulus.AuditLogging.EntityFrameworkCore.Tests`, SQLite file):
    - two "nodes" appending 40 events at once keep both chains contiguous and valid;
    - a row edited with `ExecuteUpdate` breaks verification at that row;
    - a deleted last row is caught by the anchor;
    - the business log round-trip;
    - the script identifier check.
  - Source tests: middleware rejection and break-glass (`TenantMiddlewareTests`), and the guard's denied and overridden records (`TenantDataIsolationTests`).

### Phase 5: A9 isolation test generator

- [x] **`SecurityProbeSuite.RunAsync(factory, options?)`** (`Modulus.Testing.Security`), also `RunAsync(services, client, options?)` for a hand-built TestServer host. It enumerates every route endpoint and method (`Skip` leaves endpoints out) and probes:
  - **anonymous:** `401`, or a redirect whose location contains `LoginPathFragment` (default `login`);
  - **no permission:** `403` for a signed-in caller without permissions or roles, on endpoints with a policy, permission or role (a bare sign-in requirement gets no such probe);
  - **foreign tenant** (when `ForeignTenantId` is set): `403` for a caller holding the endpoint's permissions and roles who selects a company it is not a member of.

  Loosened endpoints are listed, not probed (the startup guard and the allow-list review them). Route parameters get placeholders by constraint (`RouteValue` overrides that), and unsafe methods send `{}`. Responses are read to the headers only, so event streams do not hang. `report.EnsureNoFailures()` throws `SecurityProbeException` with one line per failure.
  - **As built:** `AnonymousStatusOverrides` lists routes refused with another code by design. Its default holds OpenIddict's `/connect/userinfo`, which answers `400 invalid_request` to a request with no token before ASP.NET authorization runs (found by the end-to-end run). Accepting any 4xx would let an unpoliced endpoint that validates its body first pass with `400`.
- [x] **Helpers** (`TenantIsolationAssertions`):
  - `services.AssertTenantIsolationAsync<TContext, TEntity>(entity, owner, other)` saves the entity as one company. As the other company it then checks that the entity is not found by key, not listed, and not returned by `IgnoreQueryFilters()` (the guard must refuse that query).
  - `AssertCrossTenantReadIsDeniedAsync(clientA, clientB, create)`: an HTTP resource created by A answers `404` / `403` to B.
  - **Not built:** cache key, storage path and "consumer rejects an unknown tenant" helpers. Those properties are covered by the framework's own unit tests (`CacheKeys`, `TenantScopedFileStorage`, the 1.4 restorer tests), so an app has nothing app-specific to assert there.
- [x] **CLI:** the app template emits `SecurityProbeTests` (in the generated `ModulePipelineSmokeTest.cs`) for every API host with the security guard (`expose_api && use_security_guard`): `SecurityProbeSuite.RunAsync(factory)` + `EnsureNoFailures()`. Covered by `Guarded_API_hosts_ship_the_security_probe_test`.
- [x] `generate-crud` per-entity isolation test. **As built:** on a host whose Program.cs calls `AddMultiTenancy(`
  (`GenerateCrudCommand.IsMultiTenantHost`), the new entity implements `IHasTenantId` and
  `tests/{App}.Tests/{Entity}TenantIsolationTests.cs` runs `AssertTenantIsolationAsync<{Module}DbContext, {Entity}>` with two
  companies created through `TenantManager` (template `module/Tests/TenantIsolationTests`; never overwritten).
- [x] **Tests:** `SecurityProbeSuiteTests` (`Modulus.Testing.Tests`) cover three cases:
  - a policed host passes every probe;
  - a `DELETE` without a policy fails the suite with `expected 401, got 204`;
  - the isolation helper passes for a tenant entity and reports an entity whose filter was removed.

### `modulus app --multi-tenancy` (design, item 2 of the remaining work)

Unblocks 1.8 and the per-entity isolation test of phase 5. Decisions (v1):

- **Scope.** `--multi-tenancy` needs `--kind api` and `--auth openiddict`. A `web` host's pages and the Web host of
  `webapp+api` have no company switcher yet (a browser sends no `X-Tenant-Id`, so pages would see no data); external
  providers have no local users to seed memberships for, and their subjects are not always GUIDs. Both are refused with a
  message until a switcher / external-membership design exists. BFFs work unchanged (`BffTenantHeaderHandler` forwards
  the selected company).
- **Resolution.** `AddMultiTenancy(t => t.UseJwtClaimResolver().UseHeaderResolver().RequireMembership())`: a token with
  `tid` stays pinned to its company; a multi-company login selects one with `X-Tenant-Id` (id or slug) and needs an active
  membership. `AddModulusSecurityContext()`. Pipeline: `UseMultiTenancy()` and `UseModulusSecurityContext()` right after
  `UseAuthentication()` (membership needs the user; authorization and handlers need the tenant).
- **Store module.** `src/Modules/{App}.Modules.Tenancy/{App}.Modules.Tenancy.Infrastructure` (infrastructure-only, like
  Identity/Webhooks/Audit): `TenancyModule` (`AddEfCoreTenantStore` on `ConnectionStrings:Tenancy`, migrations in this
  project), a design-time factory for `TenantStoreDbContext` (`TENANCY_CONNECTION`), so `modulus migrate add InitialCreate
  --module Tenancy` works, and `TenancySeeding`. Program.cs runs `MigrateTenantStoreAsync` (EnsureCreated fallback outside
  Production) before the modules, and `SeedTenancyAsync` after `SeedIdentityAsync`.
- **Seeding.** Always ensures the default company (`Tenancy:Seed:DefaultTenant`, default slug `default`) and an active
  membership in it for every user in the `Admin` role, so the seeded administrator can call the API with
  `X-Tenant-Id: default`. Idempotent; runs at every start.
- **Data.** Business entities (the example and every `generate-crud` entity of a host with `AddMultiTenancy(`) implement
  `IHasTenantId`: stamped on insert, filtered on read, guarded on write by `ModuleDbContext`. `Storage:IsolateTenants` is
  set. Existing entities are never rewritten.
- **Tests.** The generated API tests create two companies through `TenantManager`, act through a client pinned to the
  first (`CreateAuthenticatedClient(..., tenantId, pinTenant: true)`), run `SecurityProbeSuite` with `ForeignTenantId` = the
  second, and assert per-entity isolation with `AssertTenantIsolationAsync<{Module}DbContext, {Entity}>` (example entity
  and every `generate-crud` entity).

**As built.** `NewAppCommand.ResolveMultiTenancy` (refusals as above), `GenerateTenancyModule` (templates
`cli/Templates/tenancy/`), `AppModel.MultiTenancy` / `ModuleModel.MultiTenant`, and the `{{ if multi_tenancy }}` blocks
in `Program`, `AppTests`, `api.csproj` and `appsettings.json`. Both seeders (`SeedIdentityAsync`, `SeedTenancyAsync`)
run in the host context (`ICurrentTenant.Change(null)`). Covered by `MultiTenancyTemplateTests`.

**Found on the way** (all three were hidden while no generated app turned tenancy on):

- **`AddMultiTenancy` after `AddModulus` did nothing.** `AddModulus` (also `AddModulusUi`, the EF authorization store)
  `TryAdd`s `NullCurrentTenant`, which is always the host, and `AddMultiTenancy` only `TryAdd`ed `CurrentTenant`, so in
  the usual order every query saw every company and nothing was stamped. It now replaces the null default (a custom
  accessor registered earlier is kept): `CurrentTenantRegistrationTests`.
- **Nobody could sign in once tenancy was real.** The Identity user/role filters hide every account from a context that
  is not the host (deliberately fail-closed), and a token request selects no company. The token server's own actions
  (`ModulusTokenController`, `ModulusAuthorizeController`, `AccountController<TUser>`, `ModulusEndSessionController`) now
  run in the host context (`HostTenantContextAttribute`): they look up accounts, not company data. A company-owned
  account is found too, and its `tid` pins the token. The filters themselves are unchanged: `HostTenantContextTests`.
- **The foreign-tenant probe expected `403` from `/connect/userinfo`,** which OpenIddict answers during authentication,
  before the tenant is resolved (`400` for the probe's caller, and it only returns the token's own claims).
  `SecurityProbeOptions.ForeignTenantStatusOverrides` (default: that route → `400`/`403`) mirrors
  `AnonymousStatusOverrides`.

Also fixed: generated test classes reported a cleanup `ObjectDisposedException` (the minimal-hosting `Program` disposes
the container while the test host is still stopping it, and `ModuleLifecycleHostedService.StoppedAsync` resolved from it);
module shutdown is now skipped when the container is already gone.

### Log redaction (design, item 5 of the remaining work)

Goal: classified values never reach a log sink in clear text, with one vocabulary shared by endpoints, entities, DTOs
and log calls. Built on `Microsoft.Extensions.Compliance.*` and `Microsoft.Extensions.Telemetry` (MIT, dotnet/extensions,
the same 10.7 line as `Http.Resilience`).

**Taxonomy `Modulus`** (`ModulusTaxonomy`, `Modulus.Core.Abstractions.Compliance`). Two axes, because they call for
different treatment:

| Classification | Meaning | Default redactor |
|---|---|---|
| `Internal` | Company-internal (ids, slugs, codes). Same word as `DataClassification.Internal` on endpoints | none (logs are internal) |
| `Confidential` | Business-sensitive (prices, contracts, salaries in aggregate) | erased |
| `Restricted` | Most sensitive (payroll, health, bank data) | erased |
| `Personal` | Identifies a person (name, e-mail, phone, address). `[ProtectedPersonalData]` columns are this | HMAC-SHA256 when `Security:Redaction:HmacKey` is set (the same person gives the same token, so lines still correlate), else erased |
| `Secret` | Passwords, tokens, keys, connection strings | erased, **always** (also with redaction switched off) |

- The levels reuse the phase 2 `DataClassification` words (`ModulusTaxonomy.For(level)`: `Public` → none, `Unspecified` →
  unknown, which falls back to erased).
- Attributes (all `DataClassificationAttribute`s, for `[LoggerMessage]` parameters and `[LogProperties]` members):
  `[InternalData]`, `[ConfidentialData]`, `[RestrictedData]`, `[PersonalInformation]`, `[SecretData]`
  (not `[PersonalData]`: ASP.NET Core Identity has one). `[ProtectedPersonalData]` now derives from the base with
  `Personal`, so an encrypted column is also redacted when an entity is logged with `[LogProperties]`.
- Dependency: `Modulus.Core` takes `Microsoft.Extensions.Compliance.Abstractions` (DI.Abstractions + ObjectPool only).
  This ends Core's "no package dependencies" rule on purpose: the source generator recognises a classification only by its
  base type, and domain entities (which reference Core, not the web layer) must be able to carry it.

**Runtime** (`Modulus.AspNetCore`): `AddModulusRedaction(configuration)` (section `Security:Redaction`: `Enabled` (default
true), `HmacKey` (base64, at least 32 bytes, from secrets or a vault: the secrets guard catches a committed one),
`HmacKeyId`) calls `AddRedaction` with the table above and `EnableRedaction()` on logging. `Enabled: false` (a
developer's machine) keeps only the `Secret` rule.

**What it does not do, and the guard rail for it.** Redaction applies to source-generated logging only: an ordinary
`logger.LogInformation("{Email}", user.Email)` is written as is. Analyzer **MOD0002** (packed with `Modulus.AspNetCore`, like
MOD0001) warns when a member, parameter or local carrying a classification attribute (any `DataClassificationAttribute`,
`[ProtectedPersonalData]` included) is passed to an `ILogger` extension (`Log`, `LogInformation`, ...) or a log scope, and
points to a `[LoggerMessage]` method with the attribute on the parameter. Out of scope: HTTP request logging
(`Microsoft.AspNetCore.Diagnostics.Middleware` has its own redaction; Modulus does not enable request-body logging), and
exception messages (they are not classified; domain exceptions carry ids).

**CLI.** Generated hosts call `AddModulusRedaction(builder.Configuration)` and seed `"Security": { "Redaction": { "Enabled": true } }`
(the HMAC key is left to user secrets / the environment).

**As built.** As designed, plus:
- The redactors only apply where the **classification-aware generator** runs (`Microsoft.Extensions.Telemetry.Abstractions`'s;
  the built-in one silently ignores the attributes, and analyzers do not flow to a project transitively). The generated
  `Directory.Build.props` therefore references it in every project (version `FrameworkVersion.TelemetryAbstractions`, held
  equal to `Directory.Packages.props` by a test); framework projects do not take it.
- The secrets guard's default patterns gained `*HmacKey` and `*HashKey` (the existing `PersonalDataProtection:SearchHashKey`
  was not covered either).
- `SetHmacRedactor` is not experimental in 10.7, so no suppression.
- Tests: `RedactionTests` (provider mapping, HMAC determinism, switched off, weak key, real `[LoggerMessage]` and
  `[LogProperties]` calls including a `[ProtectedPersonalData]` member), `ClassifiedLogArgumentAnalyzerTests` (MOD0002),
  `RedactionTemplateTests` (API / Web / BFF hosts, settings, generator version). In a generated app off a fresh pack,
  MOD0002 fired on `logger.LogInformation("{Email}", p.Email)` and a classified `[LoggerMessage]` wrote no e-mail address.

### Verification

1. **Done (2026-10-03):**
   - `dotnet build modulus.slnx`: 0 warnings, 0 errors.
   - `dotnet test modulus.slnx --filter "Category=Unit"`: every unit test project passes.
   - `dotnet format modulus.slnx --verify-no-changes`: clean.
2. **Done (follow-up, Docker Desktop):** the phase 3 integration tests: PostgreSQL RLS (3), SQL Server RLS (3) and MySQL per tenant (1) passed on the first run; MongoDB passed after a test fix (the upserted document needs a Guid id on insert; a server-generated ObjectId does not deserialize into the model). The whole `Category=Integration` suite then also ran; its failures were all in the tests: a RabbitMQ test still asserting "dead-lettered on the first failure" (the consumer has had bounded retry since), a Redis backplane test racing node B's subscription (now warmed up first), and the CLI scaffold test, which restored a stale `1.4.0` from the machine-wide package cache (now an isolated `NUGET_PACKAGES`).
3. **Done** with the framework packed as 1.4.0 to a scratch feed and the CLI installed from it (isolated package cache):
   - `modulus app Demo --kind api --auth openiddict`: 7/7, including `SecurityProbeTests`.
   - `--kind api --auth keycloak`: 6/6.
   - `--kind webapp+api --auth openiddict`: 10/10.
   - `--kind web --auth openiddict`: 3/3.

   The probe run found the `/connect/userinfo` behaviour (phase 5) and the `/_ui/menu` guard failure on the split Web host (phase 2 note).

4. **Done:** `modulus app ShopMt --kind api --auth openiddict --multi-tenancy` off a fresh pack: builds with 0 warnings,
   9/9 tests (round trip in a pinned company, probe with `ForeignTenantId`, example-entity isolation, a non-member
   selecting a company gets `403`); after `generate-crud Order --module Catalog`, 10/10. Run in Development: the seeded
   administrator signs in with no company header, `POST` with `X-Tenant-Id: default` → `201`, listed in `default`, an
   empty list with no company or an unknown one, refresh `200`. A plain `--kind api --auth openiddict` app still passes
   7/7. Three framework defects surfaced and were fixed (see the multi-tenancy design section, "Found on the way").

   **Still to do by hand:** a hand-edited audit row fails `VerifyChainAsync` against a real database (the SQLite test
   covers it).

### Remaining work (resume here)

State on 2026-10-03 (follow-up session): phases 1–5 are built and verified, including the Docker integration tests. Done in
the follow-up: item 1 (integration tests run, see Verification), item 3 (security audit in generated apps), item 6 (every
smaller gap), item 7 (account route), item 2 (`--multi-tenancy`) and item 5 (log redaction). Open items:

1. ~~Run the integration tests on a machine with Docker.~~ Done.
2. ~~`modulus app --multi-tenancy` option.~~ Done (design and as-built notes below): 1.8, the phase 5 `generate-crud`
   isolation test and `ForeignTenantId` in the generated `SecurityProbeTests`. Follow-ups: a company switcher for pages
   (then `web` / `webapp+api`), memberships for external-provider users, and a `TenantId` data migration for entities
   created before the option.
3. ~~Security audit wiring in generated apps.~~ Done: every guarded host calls `AddModulusSecurityAudit(builder.Configuration)`
   (in-memory chain), and `modulus add-audit-store` adds the durable store module
   (`{App}.Modules.Audit.Infrastructure`: `AppAuditDbContext : ModulusAuditDbContext`, design-time factory with
   `AUDIT_CONNECTION`, `AuditModule` with `AddModulusAuditStore`), the `Audit` connection string and, in Development,
   `Security:Audit:AnchorFile = audit-anchors.jsonl`. Production anchors and the append-only scripts stay a deployment step
   (the command prints them).
4. **UI (deferred by request):**
   - Files UI paths relative to the tenant root;
   - Security tab in `Modulus.UI.AuditLogging` (chain list, `VerifyChainAsync` status per company, anchor comparison).
5. ~~Log redaction.~~ Done: see "Log redaction" (taxonomy `Modulus`, `AddModulusRedaction`, MOD0002, CLI wiring).
   Follow-ups: HTTP request-logging redaction if an app turns request logging on. The check that a project declaring
   classified `[LoggerMessage]` methods has the classification-aware generator is done: analyzer **MOD0004**
   (`ClassifiedLoggerMessageAnalyzer`, 2026-10-23) warns on each classified parameter (other than `[InternalData]`) when the
   project does not reference `Microsoft.Extensions.Telemetry.Abstractions`. It reads the project's references, so a reference
   that arrives only transitively counts as present and the warning is missed, never raised wrongly. Tests:
   `ClassifiedLoggerMessageAnalyzerTests` (3 tests, analyzer suite 21/21 on net8.0 and net10.0). Repo projects and the CLI
   templates pass: the test project references the package directly, and the generated apps' `Directory.Build.props` does too.
6. ~~Smaller gaps.~~ All done: token revocation audited, `Unscoped(reason)` audited, Mongo database per tenant,
   `OpenTenantConnectionAsync` for Dapper / read models, GraphQL fields and realtime topics in the guard report, and the
   durable audit spool.
7. ~~`AccountController<TUser>` route.~~ Fixed (`/account/...`).

How to re-verify end to end (what was done on 2026-10-03):

```
dotnet pack modulus.slnx -c Release -o <feed>
dotnet tool install Synvro.Modulus.Cli --tool-path <tools> --add-source <feed> --version 1.4.0
<tools>/modulus app Demo --kind api --auth openiddict --package-source <feed>
cd Demo && NUGET_PACKAGES=<fresh-cache> dotnet test
```

Use a fresh `NUGET_PACKAGES` folder (or delete its `cobytelabs.*` entries): the version number stays at 1.4.0, so a
cache from an earlier pack silently serves stale framework packages.

### ERP identity gaps (separate plan)
Password policy, shop-floor sign-in and bulk import, SoD rules, composite roles, positions, external-party users, SCIM, approval workflow, step-up and passkeys, sessions, device code and back-channel logout, SAML 2.0 and LDAP/AD are specified and recorded as built in [`ERP_IDENTITY_GAPS_PLAN.md`](#erp-identity-gaps-plan-and-status).

<!-- source: docs/architecture/authorization-framework-blueprint.md -->
## Modulus Authorization & Access Control Framework — Architecture Blueprint

> **Status:** Design specification. Most of what it asks for is now built; AGENTS.md (*Security model* and the
> *Security hardening* rounds) records what was built and what is still open. This is a functional and
> architectural blueprint — it defines *what* the authorization framework must do
> and *why*, not *how* it is coded. Implementation increments are tracked
> separately in `docs/archive/IMPROVEMENT_PLAN.md`; the first increment (the **permission grant
> store**) is called out in §22.
>
> **Audience:** framework architects and module authors building on Modulus.
> **Scope:** the shared authorization core that every Modulus-based application
> (ERP, CRM, HRM, MES, WMS, procurement, finance, payroll, POS, PM, asset,
> healthcare, education, government, multi-tenant SaaS) inherits and configures.

---

### 1. Executive Summary

Modern enterprise applications fail at security not because they lack checks, but
because their checks are **scattered, inconsistent, and duplicated**. The same
question — *"is this user allowed to do this, to this record, right now?"* — gets
answered in twenty different places with twenty slightly different answers. Over a
product's life this produces privilege-escalation bugs, cross-tenant data leaks,
un-auditable decisions, and a codebase that resists every new organizational
requirement.

This framework centralizes **every authorization decision** behind one consistent,
extensible model. It treats authorization as a first-class platform capability —
like persistence, messaging, or multi-tenancy — rather than a per-feature
afterthought. It answers six orthogonal questions with six composable mechanisms:

| Question | Mechanism |
|---|---|
| *Can this user use this capability at all?* | **Permissions** (capability grants) |
| *Which set of capabilities does this user have?* | **Roles & permission sets** |
| *Where in the organization can they act?* | **Organizational scope** |
| *Which records can they see/touch?* | **Data (row) scope** |
| *Which fields within a record?* | **Field-level security** |
| *Is this specific action valid on this specific object right now?* | **Resource- & workflow-aware policies** |

The design goal is **decisions as data, enforcement as infrastructure**. Business
administrators configure authorization without code changes; developers annotate
intent and never re-implement the decision engine; auditors get a single,
explainable trail of every decision.

The framework is designed to remain valid from a five-user single-company
deployment to a multi-million-user multi-tenant SaaS platform, and to absorb new
organizational models, business policies, and compliance regimes over a 10–15 year
horizon **without redesign** — only configuration and extension.

---

### 2. Vision and Design Philosophy

#### 2.1 Guiding principle — *Policy-Based Access Control (PBAC) as the substrate*

Roles and permissions are the **vocabulary** users understand; attributes and
policies are the **engine** that scales. The framework is fundamentally an
**attribute/policy-based** decision system that *presents* a familiar
role-and-permission surface. This is the architecture every mature vendor
converged on after starting with pure RBAC (see §18 anti-patterns and the
benchmark in §17): RBAC for *what you can do*, ABAC/PBAC for *which data and under
what conditions*.

#### 2.2 Design tenets

1. **Deny by default, fail closed.** Absence of a grant is a denial. An error in
   resolving context (unknown tenant, unresolved user, misconfigured policy) is a
   denial, never an accidental allow. (Modulus already enforces this at the
   tenancy layer — an unresolved tenant matches *nothing*; authorization inherits
   the same discipline.)
2. **Single decision point, many enforcement points.** There is exactly one
   conceptual **Policy Decision Point (PDP)**. It is *called* from many **Policy
   Enforcement Points (PEPs)** — HTTP endpoints, mediator pipeline, query filters,
   UI projection — but the *logic* lives in one place.
3. **Decisions are data, not code.** Roles, grants, scopes, and policy bindings
   are configuration an administrator edits at runtime. Code declares *what
   capabilities exist* and *what a policy needs to know*; it does not hard-code
   *who* gets them.
4. **Declarative intent over imperative checks.** A developer declares the
   *requirement* ("this operation needs `sales.order.approve` scoped to the order's
   business unit"). The framework decides. `if (user.IsAdmin)` scattered in
   handlers is the anti-pattern this exists to eliminate.
5. **Layered defense (positive + negative).** Coarse capability gates run first
   and cheap; fine-grained data and field checks run last and precise. A user must
   pass *every* layer — capability, scope, record, field, workflow, feature, tenant.
6. **Explainability is a feature.** Every decision can answer *why*: which grant
   allowed it, which rule denied it. This powers auditing, support ("why can't I
   see this?"), and administrator confidence.
7. **Least privilege, expressed naturally.** The default posture and the
   configuration ergonomics must make the *narrow* grant the easy grant.
8. **Performance is a design constraint, not a tuning phase.** Authorization runs
   on the hot path of every request and every row returned. The model must be
   resolvable in-memory, cacheable, and expressible as a database predicate so
   that data-scope filtering happens in the query, not in application memory.

#### 2.3 What "world-class" means here

It is *not* maximal feature count. It is the disciplined **separation of a small,
stable core** (the decision contract, the six mechanisms, the enforcement points)
from an **open extension surface** (custom scope providers, policy handlers,
attribute sources). The core rarely changes; the enterprise-specific richness
lives in extensions the core never needs to know about.

---

### 3. Business Problems Addressed

| Problem | How the framework resolves it |
|---|---|
| **Inconsistent rules** — the same permission enforced differently per screen | One PDP; all PEPs delegate to it. Consistency is structural, not disciplinary. |
| **Duplicate permission logic** | Capabilities declared once in a registry; checks reference names, never re-derive them. |
| **Hard-coded security** (`if role == "admin"`) | Roles/grants are data; code declares requirements, not identities. |
| **Complex org hierarchies** | First-class organizational scope model with hierarchical inheritance and configurable traversal. |
| **Cross-module authorization** | Shared vocabulary and decision engine; a document that flows Sales → Inventory → Finance is evaluated by the same PDP in each. |
| **Multi-company / multi-tenant** | Tenant isolation is the outermost, non-negotiable boundary; company/legal-entity scope sits inside it. |
| **Delegated authority** | Delegation is a first-class, time-bounded, auditable grant — not a shared password or a permanent role. |
| **Dynamic business policy** | Policies are attribute-driven and runtime-editable; new conditions are configuration or pluggable handlers, not releases. |
| **Regulatory compliance** | Every decision is explainable and auditable; segregation-of-duties and field confidentiality are modeled, not bolted on. |
| **Future expansion** | New scope types, attributes, and policy handlers plug in without touching the core contract. |

#### 3.1 Why role-based security alone is insufficient

RBAC answers *"what can this user do?"* but is blind to *"to which data?"* and
*"under what conditions?"*. Three failures are structural, not fixable by adding
more roles:

- **Role explosion.** Encoding data scope into roles ("Sales-Manager-North",
  "Sales-Manager-North-ExceptForVIPAccounts") multiplies roles combinatorially
  until the role catalog is unmanageable and itself a security risk.
- **No record awareness.** "Can approve orders" cannot express "…but only orders
  under €10,000, in their own business unit, that they did not themselves create"
  (segregation of duties). That is a *data + context* decision.
- **No temporal or conditional dimension.** "Can post to the ledger" must yield to
  "…only while the fiscal period is open." Roles have no concept of *when* or
  *state*.

RBAC is necessary and retained — as the human-friendly capability layer. It is
composed with organizational scope, data scope, and policy conditions to form a
complete decision. This is the RBAC→PBAC evolution every benchmark platform made.

---

### 4. Core Framework Responsibilities

The framework owns, and is solely responsible for:

1. **Authentication context** — a normalized, trustworthy description of the caller
   (identity, tenant, org placement, roles, effective permissions, attributes).
2. **A permission (capability) catalog** — the authoritative, module-contributed
   registry of what capabilities exist in the application.
3. **Grant management** — the assignment of capabilities to principals (users,
   roles, groups) and the resolution of a principal's *effective* permission set.
4. **The decision engine (PDP)** — the single component that combines capability +
   scope + record + field + condition into an allow/deny with a reason.
5. **Enforcement integration (PEPs)** — first-class hooks at the HTTP boundary, the
   application (mediator) pipeline, the data query layer, and the UI projection
   layer, all delegating to the PDP.
6. **Data-scope translation** — turning a user's authorization context into a
   **query predicate** so record filtering happens in the database.
7. **Field-level projection** — masking/omitting fields a user may not see, on
   both read (output) and write (input) paths.
8. **Feature/entitlement gating** — enabling/disabling whole capabilities by
   license, subscription, or tenant policy, *above* per-user permissions.
9. **Delegation & temporary access** — time-bounded, auditable transfer or grant
   of authority.
10. **Audit emission** — a structured, tamper-evident record of grants,
    revocations, and (configurably) decisions.
11. **The extension contract** — stable interfaces through which applications add
    scope providers, policy handlers, and attribute sources.

Explicitly **out of scope** (delegated to adjacent components, consumed by this
one): authentication protocol handling (token issuance/validation), user directory
management, and the workflow *engine* itself (the framework is workflow-*aware* but
does not orchestrate workflows).

---

### 5. Functional Capability Analysis

Each capability below states its **purpose**, **business value**, **enterprise use
cases / real-world examples**, **common challenges**, and **best practices**.

#### 5.1 Authentication Context

**Purpose.** Every decision is a pure function of *who is asking* and *what they are
asking about*. The authentication context is the trustworthy "who," assembled once
per request and immutable thereafter.

**What it must carry.** Identity (stable user id, not a display name); tenant
identity and whether the caller is operating as a **host/platform** actor;
organizational placement (which company/business unit/branch/team the user acts
within, including a possibly-multiple set); assigned roles and the **effective**
permission set derived from them; a bag of **attributes** (employment status,
clearance level, region, cost-center, manager-of relationships, licenses); the
authentication strength/method (was this MFA? a service account? an impersonation
session?); and the active delegations in effect.

**Business value.** A decision is only as good as its inputs. Centralizing the
context means every PEP evaluates against the *same* facts, and new decision
factors are added in one place.

**Use cases.** A background job posting outbox events acts as **host** with no user;
a support engineer **impersonating** a customer must be flagged so the audit trail
records both identities; a user who belongs to *two* business units needs both in
scope.

**Challenges.** (a) *Where does authority originate?* Trusting claims blindly means
whoever mints the token controls authorization — acceptable only if the token
issuer is the same trust domain. For fine-grained, editable, data-scoped decisions,
the effective permission set must be **resolved server-side from the grant store**,
not read from a self-asserted claim. (b) *Freshness.* Cached context must expire or
be invalidated when grants change, or a revoked permission lingers. (c) *Ambient
context* (tenant, impersonation) must be explicit and immutable, never mutable
global state.

**Best practices.** Normalize once at the edge into an immutable context object;
resolve effective permissions server-side (claims may *carry* identity and roles,
but data scope and fine-grained grants are resolved against the store); make
host/system and impersonation first-class, explicit flags; never let a PEP reach
around the context to raw claims.

> **Modulus today:** `ICurrentUser` (identity, roles, flat permission list) and
> `ICurrentTenant` (with the fail-closed `IsHost`) already form the spine of this
> context. The gap this blueprint fills: permissions currently arrive as JWT
> *claims* (issuer-asserted). The grant store (§22) makes the effective set
> *server-resolved and editable* — the pivotal step from static to enterprise-grade.

#### 5.2 Permission Management

**Purpose.** A permission is the atomic unit of *capability* — the smallest
"you may do X" the system recognizes.

**Permissions represent business capabilities, not technical operations.** The
right permission is `sales.order.approve`, not `UPDATE dbo.Orders`. Business
capabilities are stable, meaningful to administrators, and independent of
implementation; technical operations are volatile and meaningless to the people who
actually assign access. This single choice determines whether your permission
catalog is administrable by a business analyst or only by a developer.

**Organization & lifecycle.**
- **Hierarchical naming** — `module:group:action` (e.g., `sales:order:approve`).
  The prefix enables per-module grouping, wildcard grants (`sales:order:*`), and a
  navigable admin UI. (Modulus already uses `:`-delimited names and module-prefixed
  grouping.)
- **Grouping & categorization** — permissions roll up into functional groups for
  presentation and bulk assignment.
- **Prerequisites/implication** — a permission may *require* or *imply* another
  (approve implies read; you cannot grant approve without read). Modulus's
  `PermissionDefinition.Requires` is the seed of this.
- **Multi-valued/parametric permissions** — richer platforms attach *values* to a
  permission (SAP's authorization objects carry field values: activity 01/02/03,
  org level, amount ceilings). Modulus should support this as a permission carrying
  a small set of **constraint attributes** rather than exploding into many names.
- **Lifecycle** — permissions are declared by modules at startup and **frozen**
  (immutable at runtime) so the catalog is a stable contract; *grants* against them
  are fully dynamic. Deprecation is a first-class state (a permission can be marked
  obsolete without breaking existing grants).

**Business value.** A clean, business-meaningful, self-describing catalog is the
foundation everything else assembles from. Get this wrong and every role, every
audit, every admin screen inherits the mess.

**Challenges.** Granularity balance — too coarse and you cannot express least
privilege; too fine and administration drowns. Rule of thumb: one permission per
*business action a user would recognize*, with data/field nuance handled by the
scope/field layers, not by minting more permissions.

**Best practices.** Declare permissions in code (discoverable, versioned,
refactor-safe), assign them as data. Freeze the catalog after startup. Prefer
implication chains over duplicate grants. Reserve permission *values/constraints*
for genuinely parametric capabilities.

#### 5.3 Role-Based Authorization

**Purpose.** A role is a **named bundle of permissions** matching a job function
("Accounts Payable Clerk", "Warehouse Supervisor"). Roles are how humans reason
about and assign access at scale — nobody assigns 300 permissions one at a time.

**Where RBAC excels.** Job-function-aligned access; bulk assignment and revocation;
comprehensible administration; a natural unit for segregation-of-duties analysis
("no one may hold both *create-vendor* and *approve-payment*").

**Where RBAC breaks (and must be composed, not extended).** The moment
authorization depends on *which record* or *what condition*, roles alone force
either role explosion or coarse over-granting. See §3.1. The framework's answer is
**not** more roles — it is to keep roles as the capability layer and delegate data
and conditions to the scope and policy layers.

**Design refinements the framework adopts:**
- **Roles as data, hierarchical.** Roles can inherit (Senior Manager ⊇ Manager ⊇
  Employee), reducing duplication. Guard against deep, tangled hierarchies (a known
  anti-pattern — §18).
- **Permission sets / entitlement bundles.** A finer-grained, composable unit
  between permission and role (Salesforce's permission sets, Dynamics's stacked
  roles): additive bundles layered onto a base role so you avoid cloning a whole
  role to add one capability. This is the primary escape valve from role explosion.
- **Static vs. dynamic roles.** Static: assigned by an admin. Dynamic/computed:
  membership derived from attributes ("all users whose department = Finance"),
  re-evaluated as attributes change — powerful but must be explainable.
- **User-level grants/denies.** Direct grants and, importantly, **explicit denies**
  that override role grants (needed for compliance exceptions). Deny-overrides-allow
  must be an explicit, audited, deliberately-scarce mechanism.

**Best practices.** Keep roles business-function-aligned and *free of data scope*.
Use permission sets for the long tail. Keep role hierarchy shallow. Make role
assignment auditable and, for sensitive roles, approval-gated. Analyze roles for
segregation-of-duties conflicts as a standing control.

#### 5.4 Organizational Authorization

**Purpose.** Enterprises are structured — companies, legal entities, business
units, branches, departments, teams, warehouses, factories, cost centers, regions,
projects, buyer groups. Authorization must understand *where in that structure* a
user is entitled to act.

**The model.** A configurable **organizational hierarchy** (a tree, sometimes a
DAG — a user or unit may report into more than one parent, e.g., a matrixed
functional + geographic structure). Each user has one or more **organizational
placements**. Permissions are evaluated *relative to* a scope in this tree.

**Hierarchical access models (the traversal choices):**
- **This unit only** — act strictly within the assigned node.
- **This unit and descendants** — a regional manager sees their region and every
  branch beneath it (downward inheritance — the most common enterprise model).
- **This unit and ancestors** — rare; visibility "upward" for roll-up reporting.
- **Sibling/peer** — shared visibility across units at the same level.
- **Cross-hierarchy assignment** — explicit grants to units outside one's branch
  (a shared-services team serving multiple divisions).

**Business value.** This is what lets one role definition ("Branch Manager") serve
500 branches: the *capability* is identical; the *scope* is the manager's own node.
It collapses the role explosion that pure RBAC would suffer.

**Use cases.** A cost-center owner approves spend only within their center and
sub-centers; a factory supervisor sees work orders for their plant; a regional
sales director rolls up all territories beneath them but not peer regions.

**Challenges.** Reorganizations (units merge, split, move) must not orphan grants —
scope references must survive restructuring. DAG hierarchies make "descendants"
ambiguous and expensive; caching the hierarchy closure is usually required. Users
with multiple placements need well-defined *union* semantics.

**Best practices.** Model the hierarchy as first-class data with a cached
transitive closure; support downward inheritance as the default; make traversal
mode a property of the *grant* (this role, at this scope, with this traversal), not
a global setting; design for reorg from day one (stable unit identities, grants
that reference units not paths).

**This is distinct from tenancy.** Tenant = the outermost hard isolation boundary
(see §5.12/§13). Organization = structure *within* a tenant. A multi-company group
is typically *one tenant, many companies* (shared platform admin, controlled
inter-company visibility); independent customers are *separate tenants* (no
visibility, ever).

#### 5.5 Data Scope Authorization

**Purpose.** Given a user *can* perform an action (capability) *somewhere* (org
scope), **which specific records** may they act on? This is row-level security and
it is where most real enterprise access rules actually live.

**Common visibility models (from narrowest to broadest):**
- **Own records** — records the user created or is the assigned owner of (a sales
  rep sees their own opportunities).
- **Team records** — records owned by the user's team/group.
- **Department / business-unit / branch / company records** — records belonging to
  an organizational node the user is scoped to (composes with §5.4).
- **Assignment-based** — records to which the user is explicitly related:
  assigned customers, assigned suppliers, managed warehouses, buyer's own POs, a
  case worker's caseload, a doctor's patients. This *relationship* is the scope.
- **Hierarchy/management-based** — a manager sees records owned by anyone reporting
  to them (recursively).
- **Shared/collaborative** — records explicitly shared with the user or a group
  (Salesforce sharing rules, Dynamics access teams) — additive, manual grants on
  top of the base model.
- **Global/administrative** — no row restriction (reserved, audited, rare).

**How it must work architecturally.** Data scope is expressed as a **predicate**
that the framework composes into the data query, so the database returns only
authorized rows. It must *never* be "load everything, filter in memory" — that
leaks (via counts, pagination totals, aggregates) and does not scale. This mirrors
how Modulus already implements tenant isolation and soft-delete as EF Core global
query filters and how `GetByIdAsync` deliberately uses a filtered LINQ query
instead of `FindAsync` to avoid bypassing those filters. Data scope is the same
mechanism generalized: **the user's scope becomes a filter.**

**Business value.** This is the difference between "the sales team can read orders"
(useless — which orders?) and a real policy. It is also the primary
data-confidentiality and privacy control (a nurse sees only their ward's patients).

**Challenges.** *Performance* — scope predicates must be index-friendly; a
"management hierarchy" scope can become an expensive recursive join and usually
needs a materialized closure or denormalized owner-path. *Composition* — a user's
effective scope is often a *union* of several models (own + team + explicitly
shared), which must translate to an efficient `OR` of predicates without exploding
into a query the database cannot optimize. *Aggregates and reporting* — the same
scope must apply to counts, sums, and dashboards, not just list screens.
*Bypass paths* — every read path (search, export, API, report) must go through the
filter; the `FindAsync`-bypass lesson generalizes.

**Best practices.** Model scope as composable predicate providers keyed by entity;
apply them as query filters at the data layer, uniformly, with no opt-out on read
paths; provide a *deliberate, audited* elevation for the rare admin/global case;
denormalize ownership/org paths onto records to keep predicates index-friendly;
test scope with data-driven fixtures per model.

#### 5.6 Business Operation Authorization

**Purpose.** Beyond CRUD, enterprise systems are defined by **business operations** —
approve, reject, cancel, post, receive, issue, reverse, close, reopen, export,
import, reconcile. These are the high-value, high-risk actions, and they are where
segregation of duties and compliance concentrate.

**Why operational permissions depend on context, not static grants alone.**
"Can approve" is rarely absolute. It is bounded by: an **amount/threshold** (approve
up to €10,000; above that escalates); **segregation of duties** (cannot approve a
document you created or a vendor you set up); **document state** (can only post a
*submitted* document); **period state** (can only post while the fiscal period is
open); **org scope** (approve only within your cost center). A static "approve"
permission is the *entry ticket*; the actual decision consults record and context.

**Business value.** These controls *are* the internal-control and audit posture of
the business. Getting them right is the difference between passing and failing an
SOX/financial audit.

**Use cases.** Multi-tier purchase approval by amount; four-eyes principle on
journal posting; "maker cannot be checker" on payments; period-close locking; export
of PII requiring an elevated, logged permission.

**Best practices.** Model each business operation as its own permission (never fold
"approve" into "edit"). Express thresholds and SoD as **policy conditions** attached
to the operation, evaluated against the record and the actor — not as extra roles.
Treat state-dependent operations under workflow-aware authorization (§5.8). Log
every sensitive operation decision, allow *and* deny, with the deciding factor.

#### 5.7 Resource-Based Authorization

**Purpose.** The decision depends on the **specific object instance** and the
relationship between it and the user — not just the object's type. "Can edit
*this* document" because you own it, are responsible for it, or it is in your unit.

**Determining factors.** Ownership (creator/assignee); responsibility (current
handler, approver in the chain); organizational placement of the record;
workflow stage; and confidentiality/classification of the specific instance.

**Relationship to data scope.** Data scope (§5.5) is resource-based authorization
applied *in bulk* to filter a *set*; resource-based authorization is the *same
question* asked about a *single, already-identified* instance ("can this user open
record #4711?"). The framework must answer both with the *same rules* so a record a
user cannot see in a list cannot be opened by guessing its id — the list filter and
the single-item check must never diverge. (Again mirrored by Modulus's insistence
that `GetByIdAsync` honor the same query filters as `ListAsync`.)

**Best practices.** Derive the single-item check from the same predicate/policy as
the collection filter; make ownership/responsibility explicit attributes on the
record; treat instance-level confidentiality as an attribute the policy reads.

#### 5.8 Workflow-Aware Authorization

**Purpose.** A business document is not a static row — it moves through states
(Draft → Submitted → Approved → Posted → Closed → Archived; or Rejected/Cancelled).
*Who may do what* changes at each state.

**Examples.** An author edits a *Draft* freely; once *Submitted*, they can no longer
edit but the approver can approve/reject; once *Approved/Posted*, no one edits and
only a privileged role may *reverse*; a *Closed* period is read-only to all but a
reopen-privileged controller.

**Business value.** State-aware rules encode the actual control lifecycle of the
business and prevent the classic failures ("someone edited an approved invoice",
"a posted entry was silently changed").

**Design.** The framework is workflow-*aware*, not a workflow *engine*. It reads the
record's current state as an **attribute** and lets policies condition on it
("`edit` allowed only when state ∈ {Draft, Rejected}"). Permissible **transitions**
are themselves authorization decisions (who may move Submitted → Approved). State
comes from the domain/workflow module; the framework consumes it.

**Challenges.** Keeping state-permission matrices comprehensible as states and
roles grow (a state × role × action matrix); ensuring the transition guard and the
post-transition edit rules agree; concurrent transitions.

**Best practices.** Model the allowed (state, action) pairs as declarative policy
data, not `switch` statements in handlers; treat every transition as a permissioned
operation; surface the state-permission matrix to administrators for review.

#### 5.9 Field-Level Security

**Purpose.** Two users may both legitimately open the *same record* yet be entitled
to *different fields* of it. A recruiter sees a candidate's profile but not the
salary offer; a sales rep sees the customer but not the account's margin; a clerk
sees an employee record but not the compensation or the confidential HR notes.

**Business value.** Enables *shared records with differential disclosure* —
essential for privacy (PII/PHI minimization), commercial confidentiality (cost,
margin, pricing), and management information (performance notes, comp). Without it,
you are forced to split records or over-restrict whole documents.

**Two directions, both required.**
- **Read (output) masking** — unauthorized fields are omitted or masked in every
  projection (API responses, reports, exports), never merely hidden in one UI.
- **Write (input) protection** — a user who cannot see/edit a field cannot set it;
  attempts are rejected or ignored, never silently persisted.

**Design.** Field visibility is driven by **field security profiles** (Dynamics's
term) or field-level policies keyed by (entity, field, principal-context). Fields
carry a **classification** (public, internal, confidential, restricted); a user's
clearance/permissions determine which classifications they may read/write. Masking
happens at the serialization/projection boundary so it is uniform across every
output path.

**Challenges.** *Completeness* — every output path must mask (the export and the
report are the usual leaks). *Performance* — per-field checks across large result
sets must be resolved from cached profiles, not per-row DB lookups. *Aggregates* —
can a user who cannot see individual salaries see their *sum*? (A deliberate policy
choice the framework must let you express.) *Search* — filtering on a field a user
cannot see can leak its value through result membership.

**Best practices.** Classify fields declaratively on the model; drive masking from
profiles resolved once per request; enforce at the projection boundary for read and
at the command/validation boundary for write; treat aggregate exposure as an
explicit policy; audit access to restricted fields.

#### 5.10 User-Experience Authorization

**Purpose.** Authorization should not only *block* — it should *shape* the interface
so users see a coherent application containing only what they can actually use.
Hidden menus, disabled or absent action buttons, filtered report/dashboard lists,
context-aware commands, personalized workspaces.

**Business value.** Usability, reduced training and support ("why is this button
here if it errors?"), reduced attack surface (undiscoverable ≠ secure, but it
lowers casual probing), and a cleaner, role-appropriate product experience.

**The cardinal rule: UX authorization is convenience, never the control.** Hiding a
button is a courtesy; the *server-side PEP* is the security boundary. Every action
the UI offers must be independently enforced on the server, because the UI is
attacker-controlled. The framework must expose the *same* decision engine to the
UI (so the client can ask "may I?" to decide what to render) **and** enforce it on
the server (so a forged request fails). Two evaluations, one rule source.

**Design.** A capability-query endpoint/contract lets the client fetch the user's
effective permissions/decisions for a screen and render accordingly; the server
enforces regardless. Menu/feature visibility composes permission + feature gate
(§5.11) + scope.

**Best practices.** Never rely on hidden UI for security; drive UI and server from
the same permission source; batch capability queries per screen for performance;
degrade gracefully (disabled with explanation > mysteriously absent, for
discoverability where appropriate).

#### 5.11 Feature Management (Entitlements)

**Purpose.** A dimension *above* per-user permissions: whether a **capability,
module, or feature exists for this organization at all** — governed by licensing,
subscription tier, purchased add-ons, organizational policy, or geographic/regulatory
availability.

**Distinction from permissions.** Feature/entitlement gating asks *"is this feature
available to this tenant/organization?"*; permissions ask *"may this user use an
available feature?"*. A feature disabled by entitlement is invisible and
inaccessible to **everyone** in that tenant, including its admins — the check sits
*outside and before* the user-permission check. (This is Modulus's natural
extension of tenancy: a per-tenant feature/entitlement state.)

**Business value.** Monetization (tiered SaaS plans, add-on modules), compliance
(disable a feature not permitted in a jurisdiction), phased rollout (feature flags),
and per-tenant configuration.

**Use cases.** "Advanced analytics" only on the Enterprise plan; "e-invoicing" only
for tenants in countries that mandate it; a customer who purchased Manufacturing but
not Payroll never sees Payroll; a feature dark-launched to 5% of tenants.

**Best practices.** Model features/entitlements as a per-tenant (and optionally
per-org-unit) enabled set, resolved with tenant context; evaluate the feature gate
*before* permission checks and *before* rendering; keep the feature catalog, like
the permission catalog, module-declared; allow hierarchical defaults (plan →
tenant → unit overrides).

#### 5.12 Multi-Tenant Security

**Purpose.** Secure, absolute isolation between tenant organizations, while allowing
**platform (host) administration** across tenants and **controlled sharing** of
genuinely-common resources (master reference data, shared catalogs).

**The non-negotiable boundary.** Tenant isolation is the *outermost* check and it
**fails closed**: an unresolved or ambiguous tenant context grants access to
*nothing*. Modulus already implements exactly this — `ICurrentTenant.IsHost`, a
query filter of the form `IsHost || row.TenantId == currentTenant.TenantId` (a null
tenant matches no row), and host-written outbox rows dispatched under an explicit
host scope. The authorization framework treats this as an inviolable precondition:
*no authorization decision is even evaluated until tenant scope is established.*

**Host/platform administration.** A host actor (no tenant, `IsHost = true`) can
administer across tenants — provisioning, support, cross-tenant reporting. This is a
deliberate, audited elevation, not the default, and impersonation of a tenant user
by a host must be flagged in the audit trail with both identities.

**Controlled sharing.** Some data is legitimately shared (a global product taxonomy,
country/currency reference data). The framework must allow *explicitly-marked* shared
resources readable across tenants while everything else is isolated by default — the
sharing is opt-in per resource, never a relaxation of the global filter.

**Best practices.** Tenant filter first and unconditional; fail closed on
unresolved tenant; host access explicit and audited; shared data opt-in and marked;
never let an org/data-scope rule *widen* beyond the tenant boundary.

#### 5.13 Temporary and Delegated Access

**Purpose.** Authority is not always permanent. Real organizations need: a manager
on leave **delegating** approval authority to a deputy; **temporary** project-based
access that auto-expires; **emergency/break-glass** access granted under exception
with heightened logging; time-limited elevated admin privileges (just-in-time
admin).

**Why first-class.** The failure mode this prevents is the endemic enterprise
anti-pattern of *sharing credentials* or *granting a permanent role "just for this
week"* that is never revoked — the single largest source of privilege creep. Making
delegation a proper, **time-bounded, auditable, revocable** grant eliminates the
incentive to do it the dangerous way.

**Models.**
- **Delegation** — user A authorizes user B to act with (a subset of) A's authority,
  for a bounded window, optionally scoped to specific documents/amounts. The audit
  trail records B acted *on behalf of* A.
- **Temporary grant** — a role/permission with a validity window; automatically
  inert after expiry (no manual cleanup, no lingering privilege).
- **Break-glass** — pre-defined emergency elevation, self-service but heavily
  logged and typically alerting/ reviewed after the fact.
- **Just-in-time elevation** — a privileged capability granted only for the duration
  of an approved task/session.

**Challenges.** Sub-delegation (can B further delegate?) must be bounded;
delegation must respect the delegator's *own* limits (you cannot delegate authority
you do not have, nor beyond your threshold); expiry must be enforced at decision
time (never rely on a cleanup job alone); revocation must be immediate.

**Best practices.** Every temporary/delegated grant carries an explicit validity
window enforced at the PDP; delegated authority is capped by the delegator's own;
"on behalf of" is preserved end-to-end in the audit trail; break-glass alerts and is
reviewed; default deny on any ambiguity about whether a delegation is still valid.

#### 5.14 Auditing and Compliance

**Purpose.** Authorization is a primary source of the accountability record.
*Who was granted what, by whom, when; who accessed what; who was denied what.*

**What to capture.**
- **Administrative events** — grant/revoke of roles and permissions, role
  definition changes, delegation creation, feature toggles. These are the
  highest-value audit records (they explain *why* someone had access).
- **Access decisions** — configurably, allow/deny of sensitive operations and
  restricted-field access, with the *deciding factor* (which grant, which rule).
  Auditing *every* decision is prohibitively voluminous; the framework must let you
  scope decision-auditing to sensitive resources/operations.
- **Effective-access snapshots** — the ability to answer, at a point in time, "what
  could this user access?" and "who could access this record?" (a common auditor
  and breach-investigation question).

**Business value.** Regulatory compliance (SOX, GDPR/HIPAA access controls, ISO
27001, industry regimes); security investigation (what did a compromised account
reach?); governance (periodic access recertification — "confirm each user still
needs their access"); segregation-of-duties attestation.

**Best practices.** Emit structured, tamper-evident audit events for all
administrative changes unconditionally; make decision-auditing declaratively
scoped; retain enough to reconstruct historical effective access (or snapshot
periodically); support access *recertification* campaigns; ensure the audit trail
itself is subject to strict, separate access control and is append-only. Modulus's
existing outbox/integration-event infrastructure is the natural transport for
durable, ordered audit emission.

#### 5.15 Extensibility

**Purpose.** No framework can anticipate every organization's structure, policy, or
compliance regime. The measure of longevity is whether a new requirement is a
*configuration/extension* or a *redesign*.

**The extension surface (stable contracts the core depends on, applications
implement):**
- **Custom scope providers** — a new data-scope model (e.g., "records for customers
  in my assigned sales territory") plugs in as a predicate provider without touching
  the engine.
- **Custom policy handlers** — a new business condition (e.g., "block if the
  counterparty is on a sanctions list") plugs in as a policy the engine invokes.
- **Custom attribute sources** — new facts about the user or record (clearance,
  certification, risk score) feed the context and become available to policies.
- **Custom organizational models** — a new hierarchy type or traversal rule.
- **Pluggable stores** — grant/role/delegation persistence is behind an interface so
  it can be backed by the app's database, an external IdP/directory, or a policy
  service, without changing callers.

**Design tenet.** The core defines a *small, stable* contract (the PDP interface,
the six mechanisms, the enforcement points, the extension interfaces). Everything
enterprise-specific is an implementation of those interfaces the core never
references directly. This is precisely the module/extension philosophy Modulus
already applies elsewhere (module contribution, `TryAdd`-based overridable defaults,
provider interfaces).

**Best practices.** Extend by implementing interfaces, never by forking the engine;
keep custom logic in policy/scope/attribute providers, not in enforcement points;
version the extension contracts conservatively; ship sensible defaults that most
apps never replace.

---

### 6. Enterprise Authorization Models (how the mechanisms combine)

The framework is not "RBAC" or "ABAC" — it is a **layered composition** that
presents RBAC and evaluates as PBAC:

```
Tenant isolation        (hard boundary — fail closed, evaluated first)
   └─ Feature/entitlement gate      (is the capability available to this org?)
        └─ Capability check (RBAC)  (does the user hold the permission?)
             └─ Organizational scope (is the user entitled at this org node?)
                  └─ Data/row scope  (is THIS record in the user's scope?)
                       └─ Resource/workflow policy (is the action valid on THIS
                          instance, in THIS state, under THESE conditions?)
                            └─ Field-level projection (which fields, in/out?)
```

A request must satisfy **every** layer. Layers are ordered cheap-to-expensive and
coarse-to-fine so the common denial happens early and cheaply. Each layer is
independently configurable and independently testable. The layered composition is
the design's core intellectual property — it is what lets a *single* role
definition, plus data configuration, express access rules that pure RBAC could only
express through combinatorial role explosion.

---

### 7. Authorization Decision Flow

A single conceptual decision (the PDP), consulted by every PEP:

1. **Establish context.** Resolve tenant (fail closed if unresolved); assemble the
   immutable authentication context (identity, org placements, roles → effective
   permissions, attributes, active delegations, host/impersonation flags).
2. **Feature gate.** Is the requested capability *available* to this tenant/org? If
   not → deny (feature not entitled).
3. **Capability check.** Does the effective permission set include the required
   permission (directly, via role, via delegation, honoring explicit denies)? If
   not → deny (forbidden).
4. **Scope resolution.** For a *collection*: compose the org + data-scope predicate
   and hand it to the query (filtering happens in the database). For a *single
   instance*: evaluate the same predicate against the identified record.
5. **Resource/condition policy.** Evaluate instance-, state-, and
   condition-dependent policies (threshold, SoD, workflow state, confidentiality).
   If any denies → deny (with reason).
6. **Field projection.** Determine readable/writable fields for the context; apply
   masking on output and protection on input.
7. **Decide & explain.** Return allow/deny plus the deciding factor; emit audit as
   configured.

**Deny-override semantics:** an explicit deny at any layer beats any allow. Absence
of a grant is a deny. Errors are denies. The flow is short-circuit: the first deny
stops evaluation (except that auditing may still record it).

---

### 8. Organizational Security Concepts

- **Hierarchy as first-class data** with a cached transitive closure for efficient
  descendant/ancestor queries.
- **Placements**: a user↔unit assignment carrying the role(s) and traversal mode
  effective at that placement; a user may have several.
- **Scope traversal** as a property of the grant (unit-only / unit+descendants /
  etc.), not a global switch.
- **Reorganization resilience**: grants reference stable unit identities; moving a
  unit moves its subtree's effective access with it; splits/merges are supported
  operations, not data-repair emergencies.
- **Matrix organizations**: support a DAG where a unit has multiple parents
  (functional + geographic), with explicit union semantics for effective scope.
- **Company/legal-entity** sits at the top of the org tree *within* a tenant,
  enabling multi-company deployments with controlled inter-company visibility.

---

### 9. Data Visibility Models

The catalog of row-scope models the framework ships, all composable as a union and
all translatable to a query predicate:

| Model | Scope predicate (conceptual) | Typical use |
|---|---|---|
| Own | `record.owner = me` | Sales rep's leads |
| Team | `record.owner ∈ my team` | Team pipeline |
| Org unit (+descendants) | `record.orgUnit ∈ my scope subtree` | Branch/region visibility |
| Assignment | `record ∈ my assigned set` (customers, warehouses, caseload) | Account/territory/case work |
| Management chain | `record.owner ∈ reports-to-me*` | Manager oversight |
| Shared/collaborative | `record ∈ explicitly shared with me/my group` | Ad-hoc collaboration |
| Global/admin | `true` (audited, reserved) | Platform administration |

**Selection guidance.** Default to the narrowest model that satisfies the business
need (least privilege); layer *additive* models (own + shared) via union; reserve
global for explicitly-audited administrative roles. The *same* model set applies to
lists, single-item fetches, aggregates, exports, and reports — uniformly.

---

### 10. Business Operation Security

- Every business operation (approve/post/reverse/close/…) is its **own permission**.
- Operational limits (amount thresholds, SoD, four-eyes, period state) are **policy
  conditions** attached to the operation, evaluated against record + actor — not
  encoded as extra roles.
- Segregation of duties is modeled explicitly and analyzable: the framework can
  answer "does any principal hold a conflicting pair of capabilities?" as a standing
  compliance control.
- Sensitive operations are audited (allow and deny) with the deciding factor.

---

### 11. Field-Level Security Strategy

- Fields carry a **classification** (public/internal/confidential/restricted),
  declared on the model.
- **Field security profiles** map (classification/field, principal-context) →
  read/write permission, resolved once per request from cache.
- Enforcement at the **projection boundary** (uniform masking across API, report,
  export) for reads and at the **command/validation boundary** for writes.
- Aggregate exposure of restricted fields is an explicit policy choice.
- Access to restricted fields is auditable.

---

### 12. Workflow Security Strategy

- The framework is workflow-*aware*; it consumes document **state** as an attribute
  from the domain/workflow module.
- Allowed **(state, action)** pairs are declarative policy data, administrator-
  reviewable, not `switch` statements.
- Every state **transition** is itself a permissioned operation.
- The state-permission matrix is surfaced for governance review.

---

### 13. Multi-Tenant Security Strategy

- Tenant isolation is the **outermost, unconditional, fail-closed** boundary,
  evaluated before any other decision (Modulus's existing `IsHost`/tenant-filter
  model is the foundation).
- **Host** administration is an explicit, audited elevation; host impersonation of a
  tenant user records both identities.
- **Shared resources** are opt-in per resource; default is total isolation.
- No org/data-scope rule may ever widen access beyond the tenant boundary.
- Feature entitlements are resolved per tenant (§11/§5.11).

---

### 14. Feature Management Strategy

- Module-declared **feature catalog**, parallel to the permission catalog.
- Per-tenant (optionally per-org-unit) **enabled set**, with hierarchical defaults
  (plan → tenant → unit override).
- Feature gate evaluated **before** permission checks and **before** UI rendering.
- Drives monetization (tiers/add-ons), compliance (jurisdictional availability), and
  phased rollout (flags).

---

### 15. Delegation and Temporary Access Strategy

- Delegation, temporary grants, break-glass, and JIT elevation are **first-class,
  time-bounded, revocable, audited** grants.
- Validity windows are enforced **at decision time**, not by cleanup jobs.
- Delegated authority is **capped by the delegator's own** (and their limits);
  sub-delegation is bounded.
- "On behalf of" identity is preserved end-to-end in the audit trail.
- Break-glass alerts and is reviewed after the fact.

---

### 16. Audit and Compliance Strategy

- **Administrative changes** (grants, role edits, delegations, feature toggles) are
  always audited, tamper-evident, append-only.
- **Decision auditing** is declaratively scoped to sensitive operations/fields, with
  the deciding factor recorded.
- **Effective-access reconstruction** ("what could X access on date D?", "who could
  access record R?") is supported for investigations and audits.
- **Access recertification** campaigns are a supported governance workflow.
- Audit emission rides Modulus's durable outbox/integration-event transport; the
  audit store itself is access-controlled separately.

---

### 17. Benchmark Against Enterprise Platforms

Conceptual comparison — patterns worth adopting and pitfalls to avoid.

| Platform | Core model | Adopt | Avoid / caution |
|---|---|---|---|
| **SAP (ERP/S4)** | Authorization *objects* with field values (activity, org level, amount); roles via PFCG; derived roles for org scope | Parametric permissions carrying values; derived-role pattern for org scoping; deep SoD tooling | Notorious complexity/opacity; authorization design becomes a specialist discipline — keep the surface administrable |
| **Salesforce** | Profiles + permission sets; Org-Wide Defaults + role hierarchy + sharing rules; field-level security | **Permission sets** (additive, escape role explosion); **OWD default-deny then open up** via sharing; explicit field-level security | Sharing-rule sprawl and recalculation cost at scale; many overlapping mechanisms confuse admins |
| **Microsoft Dynamics 365** | Security roles + **business units** + **field security profiles** + **access teams** | Business-unit hierarchy as org scope; field security profiles; access teams for ad-hoc record sharing | Business-unit rigidity historically forced reorg pain — design for reorg resilience |
| **Workday** | **Domain** security policies + **business process** security; contextual security groups | Separating *functional* (domain) from *process/action* security; contextual (attribute-derived) groups | Steep model; power at the cost of a learning curve — invest in explainability |
| **ServiceNow** | Table/field **ACL** rules evaluated in order; script-able conditions | Layered, ordered ACLs; scriptable/pluggable conditions for the long tail | Script-in-ACL becomes unmaintainable — prefer declarative policy, reserve code for genuine edge cases |
| **Oracle / NetSuite / Odoo** | Roles + record-level "restrictions"/rules; subsidiary/company scoping | Record rules as query predicates; subsidiary (company) scoping within a tenant | Per-record rule proliferation; performance of dynamic rule evaluation |

**Cross-industry lessons.**
- *Everyone* started at RBAC and *everyone* added attribute/policy and row/field
  scope. Build PBAC-under-an-RBAC-surface from day one — retrofitting is the single
  most expensive authorization migration.
- **Permission sets / additive bundles** are the proven cure for role explosion.
- **Default-deny then selectively open** (Salesforce OWD) beats default-open then
  restrict — it fails safe and is easier to reason about.
- **Field-level security and org/business-unit scope** are table stakes at
  enterprise scale, not premium add-ons.
- The platforms that suffer are those where authorization became so complex it
  needed *specialists* (SAP) or where mechanisms overlap confusingly (Salesforce's
  several sharing systems). **Administrability and explainability are as important
  as expressiveness.** Optimize the model so a business analyst — not only an
  engineer — can safely operate it.

---

### 18. Common Anti-Patterns and Design Mistakes

- **Scattered checks.** Authorization logic inline in controllers/handlers/queries.
  → One PDP, many PEPs.
- **Hard-coded identities.** `if (role == "Admin")`. → Declare requirements; grants
  are data.
- **RBAC for everything.** Encoding data scope into role names → combinatorial role
  explosion. → Compose RBAC with scope/policy.
- **Filter in memory.** Loading rows then filtering by scope in application code —
  leaks via counts/pagination/aggregates and does not scale. → Scope as a query
  predicate.
- **List/detail divergence.** A record hidden from a list but openable by id. →
  Same rule for collection and instance (Modulus's `FindAsync`-bypass lesson).
- **UI-only security.** Trusting hidden buttons. → Server enforcement is the
  boundary; UI is convenience.
- **Fail open.** Unresolved tenant/user/context granting access. → Fail closed,
  always (Modulus's tenancy model is the template).
- **Permanent "temporary" access.** Standing roles granted "just for now." →
  First-class time-bounded delegation with decision-time expiry.
- **Claims as the source of truth for fine-grained grants.** Whoever mints the token
  controls authorization; grants cannot be edited or revoked without re-issuing
  tokens. → Resolve effective fine-grained permissions and scope **server-side** from
  the grant store.
- **Deep role/hierarchy tangle.** Unbounded role inheritance nobody can reason
  about. → Shallow hierarchies; prefer additive permission sets.
- **Unexplainable decisions.** "Access denied" with no reason. → Every decision
  carries its deciding factor.
- **Technical-operation permissions.** `UPDATE_TABLE_X` instead of business
  capabilities. → Model capabilities users recognize.

---

### 19. Scalability Considerations

- **Decision on the hot path.** Effective permissions must resolve from an
  in-memory, cached, per-request-immutable context — never a per-check DB round
  trip. Invalidate on grant change.
- **Row scope as SQL, not memory.** Scope predicates compose into the query and must
  be **index-friendly**; denormalize owner/org paths onto records; materialize
  hierarchy closures.
- **Union scopes** must produce predicates the database can optimize, not
  pathological `OR` trees — cap and, where needed, precompute effective scope sets.
- **Field masking** resolves from cached profiles, applied at projection — O(fields)
  per row against a cached lookup, not per-row DB access.
- **Hierarchy math** (descendants, management chains) uses cached transitive
  closures, refreshed on structural change, not recursive queries per request.
- **Caching with correctness.** Cache effective grants/roles/scope with explicit
  invalidation on administrative change (grant/revoke/role-edit/reorg). Staleness of
  a *revoked* permission is a security bug — invalidation must be prompt and, for
  revocations, may warrant push invalidation.
- **Multi-tenant fairness.** Per-tenant caches; a large tenant's authorization data
  must not evict a small tenant's or degrade shared decision latency.
- **Scale target.** The model must hold from single-company/5-user to
  multi-tenant/millions — achieved by keeping the decision *resolvable in memory* and
  the data filtering *pushed into the database*, with everything cacheable and
  invalidatable.

---

### 20. Long-Term Evolution Strategy

- **Stable core, open edges.** The PDP contract, the six mechanisms, the enforcement
  points, and the extension interfaces are the *stable* core. Enterprise richness
  arrives through scope providers, policy handlers, attribute sources, and pluggable
  stores — none of which change the core.
- **Additive, not breaking.** New scope models, attributes, and policies are added;
  existing configurations keep working. Wire/stored formats (grants, audit) get
  forward-compatible evolution, mirroring Modulus's outbox/event versioning
  discipline.
- **Externalization path.** The single PDP is the seam that lets a deployment later
  externalize decisions to a dedicated policy service (OPA-style) or integrate an
  external IdP/policy engine — because callers already depend only on the PDP
  contract, not on where decisions are computed.
- **Standards posture.** Concepts map cleanly to industry directions (PBAC/ABAC,
  externalized authorization, policy-as-data) so the framework can interoperate
  without rewrites.
- **Governance grows with it.** Recertification, SoD analysis, and effective-access
  reporting are designed in, so the framework matures into a *governance* tool, not
  just an enforcement one.

---

### 21. Recommendations — Building for the Next 10–15 Years

1. **Commit to PBAC-under-RBAC now.** Present roles and permissions; evaluate via a
   composable, attribute-aware engine. This single decision prevents the most
   expensive future migration.
2. **One PDP, many PEPs — enforce this structurally.** Make it *impossible* to
   authorize outside the engine by giving developers a declarative surface and no
   convenient alternative.
3. **Fail closed everywhere, inheriting the tenancy discipline** already proven in
   Modulus (`IsHost`, null-matches-nothing, host-scoped dispatch).
4. **Row scope is a query predicate, uniformly applied** across every read path —
   generalize the existing global-query-filter approach.
5. **Resolve fine-grained grants server-side** from an editable, revocable grant
   store; let claims carry identity and coarse roles, not the fine-grained truth.
6. **Model org scope, field security, and workflow state as first-class** from the
   start — they are table stakes, and retrofitting them is disruptive.
7. **Make delegation and temporary access first-class** to kill the permanent-
   "temporary"-access anti-pattern.
8. **Design for administrability and explainability**, not just expressiveness —
   the platforms that suffered made authorization a specialist discipline.
9. **Bake in audit, SoD, and recertification** so compliance is a configuration, not
   a project.
10. **Keep the core small and the extension surface open**, versioned conservatively,
    with defaults most applications never need to replace.
11. **Sequence delivery by dependency and risk** (see §22): the grant store and
    server-side effective-permission resolution first (they unblock everything
    else), then org scope + data scope, then field-level and workflow, then feature
    entitlements, delegation, and the governance/audit tooling.

---

### 22. Alignment with Modulus Today & Delivery Sequence

**What already exists and is reusable as the foundation:**
- `IPermissionRegistry` + `PermissionDefinition` (with `Requires` implication) —
  the **capability catalog** (§5.2). Module-declared, frozen at startup: correct.
- `ModulusPermissionPolicyProvider` + mediator `AuthorizationBehavior` +
  `[RequirePermission]` — **enforcement points** (§4.5) at the HTTP and application
  pipeline layers.
- `ICurrentUser` / `ICurrentTenant` (with fail-closed `IsHost`) — the spine of the
  **authentication context** (§5.1) and the **tenant boundary** (§5.12/§13).
- EF Core global query filters (tenant + soft-delete) and the deliberate
  `GetByIdAsync`-uses-filtered-query decision — the **data-scope mechanism**
  (§5.5/§5.7) in embryonic form, ready to generalize.

**The decisive gap:** effective permissions arrive as issuer-asserted JWT *claims*.
There is no **grant store** — no editable, revocable mapping of *which principal
holds which capability*, no roles-as-data, and therefore none of the org/data/field
layers that build on server-resolved grants.

**Delivery sequence (each increment independently shippable, tested, deny-by-default):**

1. **Permission grant store** *(next increment — the current P1 item).* Roles and
   grants as data; server-side resolution of a principal's **effective permission
   set** (roles → permissions, honoring `Requires` implication and explicit denies);
   `ICurrentUser.HasPermission` resolves against this store (claims still carry
   identity/roles). Unblocks everything below. Fail-closed; frozen catalog, dynamic
   grants.
2. **Organizational scope** — org hierarchy as data, placements, traversal modes.
3. **Data (row) scope** — composable predicate providers generalizing the existing
   query-filter mechanism; union semantics; index-friendly.
4. **Resource/workflow-aware policies** — instance/state/threshold/SoD conditions.
5. **Field-level security** — classifications + profiles, projection-boundary masking.
6. **Feature entitlements** — per-tenant feature catalog and gate.
7. **Delegation & temporary access**; then **governance tooling** (SoD analysis,
   recertification, effective-access reporting) atop the audit stream.

Each increment obeys the working agreements in `docs/archive/IMPROVEMENT_PLAN.md`: builds
0 warnings / 0 errors under global `TreatWarningsAsErrors`, ships a regression test
per behavior, and preserves fail-closed semantics.

---

*End of blueprint. This document governs the design of Modulus authorization; code,
schemas, and APIs are produced per-increment against it and reviewed for
conformance to §2 (tenets), §6 (layered model), and §7 (decision flow).*

<!-- source: docs/architecture/identity-review.md -->
## Modulus.Identity — architecture & review

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

### 1. Scope

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
- SAML/WS-Fed — only OIDC is supported. *(Changed since this review: SAML 2.0 and LDAP/AD are built, see AGENTS.md *Identity*; WS-Federation is not.)*
- Multi-tenant isolation of the identity store itself (the framework's
  multi-tenancy applies at the data layer; the user store is shared unless
  the host partitions it).

---

### 2. The single-external-provider invariant

> **One app, one external IdP.**

A Modulus app may register **at most one** external identity provider. Calling
both `AddAuthentik(...)` and `AddOkta(...)`, for example, is rejected at
startup.

#### Why this rule exists

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

#### How it's enforced

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

### 3. OpenIddict server

`AddModulusOpenIddict(services, configuration, configure?)` wires
`ModulusTokenController` (token endpoint) + `ModulusUserInfoController`
(userinfo endpoint) via OpenIddict's AspNetCore host.

#### Grants

| Grant | Default | Configuration |
|-------|---------|---------------|
| Authorization code | on | — |
| Refresh token | on | `Identity:EnableRefreshToken` |
| ROPC (password) | **off** | `Identity:AllowPasswordFlow` |

ROPC is removed in OAuth 2.1. The flag exists for first-party trusted clients
that genuinely cannot use the authorization-code flow (e.g. some CLI scenarios).

#### Certificates

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

#### Token endpoint security

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

### 4. External IdP design

#### Contract

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

#### Token validation

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

#### Per-provider extension pattern

Each adapter ships an `AddXxx(this AuthenticationBuilder, IConfiguration)`
extension that:

1. Binds the provider's options from `Identity:ExternalProviders:Xxx`.
2. Registers `services.Configure<XxxOptions>(...)`.
3. Registers `services.AddHttpClient<XxxIdentityProvider>()`.
4. Registers `services.AddScoped<IExternalIdentityProvider, XxxIdentityProvider>()`.
5. Wires the OIDC handler via `builder.AddOpenIdConnect("Xxx", ...)`.

#### Discovery URLs (per provider)

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

### 5. Mental model: federated login vs delegated issuance

> ⚠️ Read this section before wiring an external IdP. The framework's external
> adapters are for **federated login**, not for delegating your token issuance.

Two patterns exist for combining an external IdP with your own OpenIddict
server, and they are not the same:

#### Pattern A — Federated login (what the adapters do today)

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

#### Pattern B — Delegated issuance (not what the adapters do)

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

#### What this means for users

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

### 6. Provider matrix

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

### 7. Wiring snippets

For each provider: install the `Modulus.Identity` package, then call one
`AddXxx`. Add **only one** — see §2.

#### Authentik

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

#### Auth0

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

#### Okta

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

#### Azure AD (Entra ID)

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

#### Duende IdentityServer

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

#### Keycloak

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

### 8. Findings & remediation

| # | Finding | Severity | Status |
|---|---------|---------|--------|
| 1 | `AuthentikIdentityProvider` wrote the bearer token to `HttpClient.DefaultRequestHeaders.Authorization`, mutating shared state across concurrent calls (race) | high | **Fixed** (this batch) |
| 2 | `OktaIdentityProvider`, `AzureAdIdentityProvider`, `KeycloakIdentityProvider` had the same shared-HttpClient race | high | **Fixed** (this batch) |
| 3 | `AuthentikIdentityProvider` set no `NameClaimType` / `RoleClaimType` / `MapInboundClaims = false` on the OIDC handler, so `User.Identity.Name` and role mapping fell back to defaults that don't match Authentik's claim shape | medium | **Fixed** (this batch) |
| 4 | No XML doc on `AuthentikIdentityProvider` class (siblings had one) | low | **Fixed** (this batch) |
| 5 | Calling two `AddXxx` extensions silently last-wins on the `IExternalIdentityProvider` service — confusing wrong-provider failure deep in login | high | **Fixed** (this batch) — `SingleExternalProviderGuard` fails fast at startup |
| 6 | No provider-level unit tests for any adapter (only `ExternalTokenValidator` shared logic tested) | medium | Partly addressed — Authentik adapter covered (mapping + race regression). Others remain |
| 7 | `DuendeIdentityProvider.GetUserBySubjectAsync` calls `/connect/userinfo` with **no** `Authorization` header — the call is unauthenticated, so it returns whatever the endpoint's default policy allows (likely an error or an anonymous principal). The other adapters attach an admin/management token; Duende does not. | medium | **Fixed.** The call now sends a client-credentials bearer token (`GetAccessTokenAsync`); transport failures return `null` ("user not found") rather than throwing. |
| 8 | No codified Pattern B (delegated issuance — Authentik login → OpenIddict-minted tokens) | medium | **Built** as `AddModulusFederatedLogin<TUser, TRole>` (token-exchange grant `urn:modulus:params:oauth:grant-type:external-token` at `/connect/token`; off until `Identity:FederatedLogin:Enabled`). Decisions: the provider's token is validated against its discovery document and the configured audiences; accounts link by the provider subject (stored as an `IdentityUserLogins` row, the provider's tokens are not stored); an email links an existing account only when the provider verified it and `LinkByVerifiedEmail` is on; mapped groups (`RoleMap`) sync roles, unmapped roles are never touched; new accounts have no password. Works with any OpenID Connect provider through `Identity:FederatedLogin:Provider`, whose profile sets the claim names: `Generic`, `Authentik`, `AzureAd` (subject `oid`, roles `roles`/`groups`), `Auth0` (set `RolesClaim` to your namespaced claim), `Okta`, `Keycloak` (roles under `realm_access.roles`), `Duende`, `OpenIddict` (roles `role`). Every claim can be overridden. Caveat: an OpenIddict server encrypts its access tokens by default, so the client must send the ID token there (or that server sets `EncryptAccessTokens` to false). Unit-tested (`FederatedLoginTests`, `FederatedProviderProfileTests`, 33 cases). Not checked against live providers. Not exercised over HTTP in the suite: the token endpoint path needs a live OpenIddict host and an IdP. Clients must also hold the grant-type permission (`AddGrantTypePermissions`, see `AddModulusFederatedLogin`'s doc comment). |
| 9 | `samples/Ecommerce` does not wire any external IdP, so users have no end-to-end reference for the federated-login pattern | low | Open. Recommend adding an Authentik-wired sample once Authentik is the most-requested adapter. |
| 10 | `KeycloakIdentityProvider.ValidateTokenAsync` uses RFC 7662 introspection but has no unit test for the introspection path (only OIDC-discovery providers are covered by `ExternalTokenValidatorTests`) | low | Open. |

#### Things deliberately not changed

- The `IExternalIdentityProvider` service-registration shape stays
  singleton-per-provider. The single-provider invariant makes any richer
  model (keyed services, named registrations, per-request selection) unnecessary.
- The `Identity:ExternalProviders:Xxx` config section naming stays as-is —
  no auto-discovery helper. Under single-provider, calling one `AddXxx` is
  simpler than indirection through a registry.

---

### 9. Testing

- `tests/unit/Modulus.Identity.Tests/ExternalTokenValidatorTests.cs` — pure
  JWT validation logic (5 cases, real RSA keys).
- `tests/unit/Modulus.Identity.Tests/AuthentikIdentityProviderTests.cs` —
  Authentik user mapping, missing-field handling, race-safety regression,
  request-URL correctness (5 cases).
- `tests/unit/Modulus.Identity.Tests/SingleExternalProviderGuardTests.cs` —
  the guard (zero/one/two providers + opt-out flag + stop, 5 cases).
- `tests/unit/Modulus.Identity.Tests/IdentityDefaultsTests.cs` — defaults
  of `ModulusIdentityOptions`.

Integration tests for the OpenIddict token endpoint are now present (`TokenEndpointHttpTests` runs
every grant against an in-process OpenIddict server). The external OIDC sign-in flow still has no
such test. Standing them up requires either Testcontainers
against a real IdP or a mock OIDC authority (e.g. `IdentityServer8` test host).
Recommended as a follow-up.

---

### 10. Mental checklist before shipping an app

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

<!-- source: docs/archive/DUAL_TARGETING_PLAN.md -->
## Dual-Targeting Plan: net8.0 + net10.0

> **Status: completed (archived 2026-10-11).** As-built notes live in AGENTS.md → "Dual targeting".

Derived from the 2026-07-26 NuGet-packaging review. Goal: every publishable
`src/` package builds and restores cleanly for **both** `net8.0` and `net10.0`
consumers, so the framework isn't limited to .NET 10-only apps. `cli/` is
explicitly **out of scope** — it stays `net10.0`-only.

**Status: implemented.** Every `src/` library except Realtime (+ Redis) and the analyzer multi-targets `net8.0;net10.0`. The UI packages left with the older UI (commit `44befc6`). See `AGENTS.md` (*Dual targeting*). The CI net8 leg is `unit-net8` in `.github/workflows/ci.yml`.

The sections below are the plan as written before implementation, kept for the reasoning. Where they describe the current state, read the status line above.

### Current state

Before this work, every `src/` project (45 csproj files today) hard-pinned
`<TargetFramework>net10.0</TargetFramework>` via `build/Modulus.Common.props`.
`Directory.Packages.props` pins a single version — mostly `10.0.9`/`10.x` — for
every package, including ones whose major version is locked to the runtime
(ASP.NET Core shared-framework packages, EF Core design-time tooling).

One artifact of an earlier, half-finished attempt already exists:
`src/core/Modulus.AspNetCore/Modulus.AspNetCore.csproj` has a
`Condition="'$(TargetFramework)' == 'net8.0'"` branch for Swashbuckle that's
currently dead code, since the TFM is singular.

### Why this isn't a version-bump — it's per-package conditional versioning

ASP.NET Core / EF Core packages that ship as part of, or bind directly to, the
shared framework are versioned in lockstep with the runtime major version —
confirmed a `10.0.x` release of these cannot be referenced from a `net8.0`
project. Central Package Management (CPM) supports this via
`Condition="'$(TargetFramework)'=='net8.0'"` on `<PackageVersion>` entries, so
the fix is per-package, not a single global downgrade.

### Package-by-package scope

**Runtime-locked — needs a net8.0 + net10.0 version pair in `Directory.Packages.props`:**

| Package | Used by | Net10 (current) | Net8 target |
|---|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | `Modulus.AspNetCore` | 10.0.9 | 8.0.x latest patch |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `Modulus.SignalR.Backplane` | 10.0.9 | 8.0.x latest patch |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | `Modulus.Identity` | 10.0.9 | 8.0.x latest patch |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | `Modulus.Identity` | 10.0.9 | 8.0.x latest patch |
| `Microsoft.AspNetCore.Mvc.Testing` | `Modulus.Testing` | 10.0.9 | 8.0.x latest patch |
| `Microsoft.EntityFrameworkCore.Design` | (CLI templates / dev-time only — not a `src/` PackageReference today, confirm before scaffolding tooling touches it) | 10.0.9 | 8.0.x latest patch |

**ASP.NET Core-adjacent, needs verification but likely fine as multi-target (confirm during implementation, don't assume):**

| Package | Used by |
|---|---|
| `Microsoft.EntityFrameworkCore`, `.Relational`, `.SqlServer`, `.Sqlite`, `.InMemory` | `Modulus.EntityFrameworkCore`, `Modulus.Mediator`, `Modulus.Authorization.EntityFrameworkCore`, `Modulus.MultiTenancy.EntityFrameworkCore`, `Modulus.Data.SqlServer`, `Modulus.Data.SQLite`, `Modulus.Testing` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `Modulus.Data.PostgreSQL` |
| `MySql.EntityFrameworkCore` | `Modulus.Data.MySQL` |
| `Microsoft.Azure.SignalR` | `Modulus.SignalR.Backplane` |
| `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore` | `Modulus.Identity` |

EF Core's own GitHub issue history shows the *Design* package needed an
explicit min-dependency fix to unblock net8 consumers — implying the core EF
runtime packages already multi-target net8/9/10 normally. Still needs a
restore-and-build check per package, not an assumption.

**Not runtime-locked, single version is fine:** `Microsoft.Extensions.*`,
`MongoDB.Driver`, `StackExchange.Redis`, `RabbitMQ.Client`, `Confluent.Kafka`,
`FluentValidation(.DependencyInjectionExtensions)`, `ErrorOr`,
`OpenTelemetry*`, `Polly`, `Microsoft.Extensions.Http.Resilience`,
`AWSSDK.S3`, `Azure.Storage.Blobs`, `Asp.Versioning.Mvc(.ApiExplorer)`,
`Microsoft.FeatureManagement.AspNetCore`, `Swashbuckle.AspNetCore`,
`Microsoft.IdentityModel.*`, `System.IdentityModel.Tokens.Jwt`, `Cronos`,
`Rebus*`.

### Execution steps

1. **`build/Modulus.Common.props`**: change
   `<TargetFramework>net10.0</TargetFramework>` to
   `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` (the shared default
   every `src/` project inherits). Exclude `cli/` (it imports its own props
   or is excluded from this import already — verify).
2. **`Directory.Packages.props`**: for each runtime-locked package in the
   table above, split the single `<PackageVersion>` into two entries gated by
   `Condition="'$(TargetFramework)'=='net8.0'"` /
   `Condition="'$(TargetFramework)'=='net10.0'"`.
3. **Per-project conditional `PackageReference`s where the package itself
   differs by TFM**, not just the version (e.g. the existing
   Swashbuckle-vs-built-in-OpenApi split in `Modulus.AspNetCore.csproj` —
   confirm no other project needs an equivalent branch, e.g. anywhere using
   .NET 10-only OpenApi transformer APIs, params collections, or other
   net10-exclusive BCL/ASP.NET Core APIs).
4. **Source-level compat audit**: grep for any net10.0-only APIs currently in
   use (the `AuthorizeCheckOperationTransformer` / `ModulusOpenApiDocumentTransformer`
   in `src/core/Modulus.AspNetCore/OpenApi/` are built against the .NET 10
   `Microsoft.AspNetCore.OpenApi` transformer pipeline — need a Swashbuckle
   equivalent code path or `#if NET10_0` guards for net8.0). This is likely
   the largest actual code-change surface, not the csproj/CPM plumbing.
5. **Restore + build both TFMs** (`dotnet build -f net8.0`, `-f net10.0`) and
   fix fallout package-by-package rather than assuming the "likely fine"
   table is correct.
6. **Tests**: decide whether `tests/` also multi-targets (run the full suite
   against both TFMs) or stays net10.0-only and just validates the net10
   build — recommend multi-targeting tests too since behavioral drift between
   TFMs (e.g. the OpenApi/Swashbuckle branch) is exactly what tests should
   catch.
7. **Samples**: `samples/*` currently pin their own `Directory.Build.props` —
   decide whether they demonstrate net8.0 consumption too, or stay net10.0
   reference apps (lower priority than the framework packages themselves).
8. **CI**: build matrix needs both TFMs (and likely both a .NET 8 SDK and
   .NET 10 SDK installed in the runner) — check current workflow file(s) for
   what needs updating.

### Open questions (as written before implementation)

- Does the .NET 8 SDK need to be installed alongside .NET 10 in dev/CI
  environments, or does `net8.0` targeting just need the runtime pack (no
  separate SDK)?
- Should package `Version`/`VersionSuffix` in `Modulus.Packaging.props` stay
  single per package (one `.nupkg` with two `lib/net8.0` and `lib/net10.0`
  folders — the normal multi-target NuGet layout), confirming this is the
  intended packaging shape rather than two separate packages per TFM.
- Scope check: does "the framework" mean all of `src/` uniformly, or could
  some platform-specific packages (e.g. `Modulus.AspNetCore.Redis`,
  `Modulus.SignalR.Backplane`) reasonably stay net10.0-only if their net8.0
  equivalents are impractical?

<!-- source: docs/archive/IMPROVEMENT_PLAN.md -->
## Modulus Improvement Plan

> **Status: completed (archived 2026-10-11).** All P0 items are done. This file is kept as the decision log.
> Open work is tracked in [`../PRODUCTION_READINESS_PLAN.md`](#production-readiness-review-findings-and-plan).

Derived from the 2026-07-12 architectural review. P0 items block any 1.0 / public
NuGet release; they are ordered by execution order (cheap trust fixes first,
restructures last). Each item records the design decision so future sessions
don't re-litigate it.

### Status

**All six P0 items are complete** (2026-07-12), building with 0 warnings / 0 errors
and 126 passing unit tests (regression tests added for each fix). Details below.

### P0 — Critical (this pass) — ✅ DONE

#### P0-3. Module bootstrap: eager graph build + single instantiation
**Problem.** (a) `ModuleLifecycleHostedService` initializes from `ModuleLoader._sorted`,
which is only populated when the app remembers to call `UseModulus()` — forgetting it
boots cleanly and silently skips every module's `InitializeAsync`. (b)
`ModulusBuilder.GetDeclaredDependencies` `Activator.CreateInstance`s every module just
to read the `DependsOn` property, then `AddModule` instantiates it again.

**Decision.** `AddModulus` builds the graph eagerly and registers a pre-built
`ModuleLoader` singleton instance; module instances are created once during discovery
and reused for registration. `UseModulus()` stays as a source-compatible no-op
(validation only). The `IModule.DependsOn` property and `[DependsOn]` attributes are
no longer supported — use explicit `AddModule<>()` registration order instead.

#### P0-6. Production-safe defaults
- Identity: `UseDevelopmentCertificates` must be false in Production; the
  `DevelopmentCertificateGuard` hosted service fails fast at startup if true.
  ROPC password flow is opt-in (`Identity:AllowPasswordFlow`, default **false**) —
  removed in OAuth 2.1.
- `MigrateModulusDatabasesAsync` default mode changes `MigrateOrCreate` → `Migrate`
  (fail loudly in production); the app template passes `MigrateOrCreate` explicitly
  in Development only.
- Rename public API typo `DecororateIntegrationEventHandlers` →
  `DecorateIntegrationEventHandlers`.
- Verify `PermissionRegistry.Freeze()` is actually invoked after module registration.

#### P0-1. Fail-closed multi-tenancy
**Problem.** Query filter `TenantId == null || e.TenantId == TenantId` means an
*unresolved* tenant (missing header, misconfigured resolver, background job that
forgot `Change(...)`) reads **every** tenant's rows.

**Decision.** Introduce an explicit host context: `ICurrentTenant.IsHost`.
- `NullCurrentTenant` (multi-tenancy not configured) ⇒ `IsHost = true` ⇒ match-all —
  single-tenant apps are unaffected.
- `CurrentTenant` (multi-tenancy configured): unresolved ⇒ `IsHost = false`,
  `TenantId = null` ⇒ filters match **nothing** (fail-closed).
- `Change(null)` (or resolver returning host) ⇒ explicit host scope ⇒ match-all.
- Filters become `e => currentTenant.IsHost || e.TenantId == currentTenant.TenantId`
  (a null parameter compares false against every row — fail-closed by construction).
- `OutboxProcessor` wraps host-written rows (`TenantId == Guid.Empty`) in an explicit
  `Change(null)` scope during dispatch.
- Mongo tenant filter gets the same semantics.

#### P0-4. Transaction scoping
**Problem.** `TransactionBehavior` opens a transaction on **every** registered
`DbContext` for **every** command — O(modules) connections/transactions per request,
and the multi-context commit loop is pseudo-atomic anyway.

**Decision.** New `[Transactional(typeof(CatalogDbContext), ...)]` attribute declares
the contexts a command touches. Defaults: exactly one registered context ⇒ wrap it
(previous behaviour for the common case); multiple contexts + no attribute ⇒ **no
wrapping** (each `SaveChangesAsync` is still atomic); `MediatorOptions.TransactionMode
= TransactionMode.AllContexts` restores the old fan-out for apps that want it.
Attribute lookups in Transaction/Caching/Authorization behaviors are cached in static
fields of the closed generic (they currently reflect per call).

#### P0-2. Stable event wire format
**Problem.** Outbox rows and broker envelopes identify events by
`AssemblyQualifiedName` — an assembly version bump or type move breaks every pending
message; `Type.GetType` on stored strings is also a deserialization surface. The
envelope drops `TenantId`/`CorrelationId` at the broker boundary.

**Decision.** Stable event names are the wire contract:
`[IntegrationEventName("catalog.product-created.v1")]`, defaulting to the type's
**full name without assembly**. `IIntegrationEventRegistry` maps name ⇔ type and is
the only resolver on the consume path (legacy AQN fallback: `Type.GetType` restricted
to already-loaded assemblies). `IntegrationEventEnvelope` gains `TenantId` and
`CorrelationId`; outbox rows store the stable name and correlation id and both are
restored on dispatch.

#### P0-5. Split Modulus.Platform
**Problem.** Wanting multi-tenancy forces AWSSDK.S3 + Azure.Storage.Blobs + Minio +
StackExchange.Redis + Azure SignalR into the dependency tree.

**Decision.** Platform keeps only shared-framework-dependency features
(MultiTenancy, Authorization, BackgroundJobs, Http resilience, memory caching,
storage abstractions + LocalFileStorage). New packages: `Modulus.Storage.AwsS3`,
`Modulus.Storage.AzureBlobs`, `Modulus.Caching.Redis`, `Modulus.SignalR.Backplane`. Namespaces unchanged where possible.
(As built: there is no MinIO package; S3 is `Modulus.Storage.S3`, and the SignalR backplane is `Modulus.SignalR.Backplane`.)

### P1 — in progress

#### P1-1. Module lifecycle phases — ✅ DONE
`IModule` gains `PreConfigureServices` and `PostConfigureServices` (default
interface methods, no-op; virtual on `ModulusModule`). `ModulusBuilder.Complete()`
now runs three ordered phases across all modules — Pre for all, then Configure for
all, then Post for all — each in dependency order. Registration is deferred from
`AddModule` into `Complete()` (always called by `AddModulus`), so a module can seed
shared state in Pre that a later module refines in Configure, and finalize in Post
once every registration is visible. Direct `IModule` implementers are unaffected
(DIM no-op). Regression: `ModuleLifecyclePhasesTests` (phase order + per-phase
dependency order + bare-impl default). 129 unit tests green.

#### P1-2. EF-backed tenant store — ✅ DONE
New package `Modulus.MultiTenancy.EntityFrameworkCore` (references Modulus.Platform
+ EF Core.Relational — keeps EF Core out of Platform). Ships `TenantEntity`,
`TenantStoreDbContext` (unique `Slug` index, `ModulusTenants` table), `EfTenantStore
: ITenantStore` (only **active** tenants resolve — a deactivated tenant fails closed
to null), `TenantManager` (create/set-active provisioning), and
`AddEfCoreTenantStore(configure)` which supersedes `NullTenantStore` via
`RemoveAll` + scoped registration. Deliberately registered only as
`TenantStoreDbContext` (never as `DbContext`) so it stays out of the module
transaction fan-out (P0-4) and module migration loop; a dedicated
`MigrateTenantStoreAsync` initialises its schema. Regression: `EfTenantStoreTests`
(7, SQLite in-memory) — supersedes null store, find by id/slug, unknown→null,
deactivated→fail-closed, duplicate-slug throws. 136 unit tests green.

#### P1-3. Registration-time entity→context map — ✅ DONE
`EfRepository<T>` previously resolved its `DbContext` by calling
`GetServices<DbContext>()` and scanning every module context's `.Model` on each
repository's first access — which forced instantiation of **every** registered
module context in the scope just to find the one owning entity `T`. New
`IEntityContextMap` (in `Modulus.EntityFrameworkCore`) records, per
`AddModuleDatabase<TContext>` call, which context owns which entities; the map is
built **once** for the app lifetime (singleton, lazy — one throwaway scope reads
each context's metadata model, no DB connection) and then serves O(1) lookups.
`EfRepository<T>` now resolves exactly the owning context and only falls back to
the runtime scan for contexts registered outside `AddModuleDatabase`. First-match
semantics preserved for framework entities shared across every context (e.g.
`OutboxMessage`). Regression: `EntityContextMapTests` (3) — routes each entity to
its owning context type, unknown→null, and **resolving a repository instantiates
only the owning context** (a construction counter proves the unrelated module
context is never built). 139 unit tests green.

#### Authorization framework — design blueprint (2026-07-12)
`docs/architecture/authorization-framework-blueprint.md` is the governing
functional/architectural spec for Modulus authorization (benchmarked against SAP,
Salesforce, Dynamics 365, Workday, ServiceNow, Oracle/NetSuite/Odoo). It defines a
**PBAC-under-an-RBAC-surface** model layered as: tenant isolation → feature gate →
capability (RBAC) → org scope → data/row scope → resource/workflow policy → field
projection, evaluated by a single PDP with many enforcement points, fail-closed
throughout. §22 maps it onto what Modulus already has (permission registry, policy
provider, `AuthorizationBehavior`, `ICurrentUser`/`ICurrentTenant`, EF query
filters) and fixes the delivery sequence. The **permission grant store** is
increment 1 of that sequence and unblocks the org/data/field/workflow layers.

#### P1-4. Permission grant store — ✅ DONE (auth blueprint §22, increment 1)
Grants are now editable/revocable **data** resolved **server-side**, closing the
"effective permissions arrive only as issuer-asserted JWT claims" gap. In
`Modulus.Platform` (`Authorization/Grants/`): `PermissionGrant` (allow/deny,
role/user holder) + `PrincipalGrantQuery`; `IPermissionGrantStore` with the default
`InMemoryPermissionGrantStore` (grant/deny/revoke for roles & users; empty ⇒
fail-closed deny-all; dynamic — mutable at runtime, unlike the frozen catalog);
`IPermissionResolver`/`PermissionResolver` computing a principal's **effective set**:
deny-override (explicit deny beats allow, applied *after* the closure), `Requires`
**implication** closure (approve ⇒ update ⇒ read, transitive, cycle-safe via a
lazily-snapshotted Requires index), and `module:group:*` **wildcard** expansion over
the registry (unknown prefix ⇒ nothing). `AddModulusAuthorization` registers store +
resolver via `TryAdd` (an EF-backed store can supersede later); `AddPermissionGrants`
seeds grants (accumulating seed pattern mirroring `IPermissionRegistration`). In
`Modulus.Identity`: `GrantStorePermissionChecker : IPermissionChecker` (opt-in via
`AddGrantStorePermissionChecker`) resolves the current principal from **user id + role
claims** (not fine-grained permission claims), scoped so the set is computed once per
request; `ClaimsPrincipalCurrentUser` already consults `IPermissionChecker`, so
`ICurrentUser.HasPermission` (and thus `[RequirePermission]`/`AuthorizationBehavior`)
now flows through the grant store. Fail-closed throughout (unauthenticated ⇒ empty).
Regression: `Modulus.Platform.Tests` (new project — 19: resolver allow/deny/
implication/wildcard/case/fail-closed + store combine/revoke/dynamic) and
`GrantStorePermissionCheckerTests` (5, DI end-to-end through the public checker seam).
**163 unit tests green**, 0 warnings / 0 errors.

#### P1-5. Organizational scope — ✅ DONE (auth blueprint §22, increment 2)
The org layer of the pipeline (blueprint §5.4, §7, §8): given a user *can* act, it
answers *where in the organization* they may. In `Modulus.Platform`
(`Authorization/Organization/`): `OrgUnit` (stable id + `ParentIds` — a DAG, so
matrixed functional+geographic structures work) with `OrgScopeMode` (UnitOnly /
UnitAndDescendants / UnitAndAncestors — traversal is a property of the *placement*,
not a global switch) and `OrgPlacement` (user↔unit↔mode). `IOrgHierarchy` +
`InMemoryOrgHierarchy` hold the tree/DAG with **memoised transitive closures**
(`Descendants`/`Ancestors`, cycle-safe), runtime-mutable via `AddUnit`/`MoveUnit`
(reorg moves a subtree's effective access because placements reference the stable
unit id, never a path). `IOrgPlacementStore` + `InMemoryOrgPlacementStore`
(`Place`/`Remove`, empty ⇒ fail-closed). `IOrgScopeResolver`/`OrgScopeResolver`
computes an `OrgScope` = **union of every placement expanded by its mode over the
closure**; anonymous or no-placement ⇒ `OrgScope.None`; a placement at an unknown
unit still scopes to that unit (explicit data) but traverses to nothing
(fail-closed). `AddModulusAuthorization` `TryAdd`s hierarchy + placements + resolver
(EF-backed stores can supersede); `AddOrganization(hierarchy, placements)` seeds
both (accumulating). No enforcement point yet — `OrgScope` is the input the **data/row
scope** layer (increment 3) composes into a query predicate (`record.OrgUnitId ∈
scope`); that increment also wires current-user → scope at the edge (mirroring the
increment-1 `IPermissionChecker` bridge). Regression: `InMemoryOrgHierarchyTests`
(9: transitive down/up, DAG multi-parent, reorg via MoveUnit, cycle termination,
unknown→empty, self-parent rejected) + `OrgScopeResolverTests` (9: fail-closed
anon/no-placement, each traversal mode, default mode, multi-placement union, unknown
unit, runtime replacement). **181 unit tests green**, 0 warnings / 0 errors.

#### P1-6. Data / row scope — ✅ DONE (auth blueprint §22, increment 3)
The enforcement point for org scope: turns the resolved `OrgScope` into an EF **query
predicate** so `record.OrgUnitId ∈ scope` is applied to every read — list and
single-item alike, because both flow through the global query filter (blueprint tenet:
row-scope-as-query-predicate, never in-memory; list & single use the *same* rule). New
`IHasOrgUnit` entity marker (`Modulus.Core.Abstractions.Entities`, mirrors
`IHasTenantId`). New `ICurrentDataScope` seam (`Modulus.Core.Abstractions`) exposing
`IsUnrestricted` + `OrgUnitIds`, with `NullCurrentDataScope` (unrestricted ⇒ filter is
a no-op when scoping isn't configured, mirroring `NullCurrentTenant.IsHost`). The edge
bridge `CurrentDataScope` (`Modulus.Platform`, scoped) resolves the current user →
`OrgScope` via `ICurrentUser` + `IOrgScopeResolver`, request-consistent (memoised);
`IsUnrestricted` gated by the `data:scope:bypass` grant (deny-by-default); opt-in via
`AddOrgDataScope()`. `ModuleDbContext.ApplyQueryFilters` refactored from a
per-combination switch to a **compositional builder** (`SetCombinedFilter<T>`): AND-
combines the layers an entity opts into — soft-delete ∧ tenant ∧ org-scope — via
`EF.Property<T>` sub-predicates + a `ParameterReplacer` that unifies only the entity
parameter, keeping each layer's context-rooted closure intact so EF re-roots it per
query (no frozen-model leak). Fail-closed: once scoping is wired, an unauthenticated or
unplaced principal sees no `IHasOrgUnit` rows; cross-org visibility requires the bypass
grant. Regression: `CurrentDataScopeTests` (4: fail-closed anon, resolved units, bypass,
request-consistent memoisation) + `OrgScopeFailClosedTests` (2: fail-closed/unrestricted
with list+single same-rule + cross-scope re-rooting; org ∧ tenant ∧ soft-delete
compose). **187 unit tests green**, 0 warnings / 0 errors.

#### P1-7. Resource / workflow policy — ✅ DONE (auth blueprint §22, increment 4)
Instance-level authorization (blueprint §5.7 resource-based + §5.8 workflow-aware):
given a user *can* act and is *in scope*, may they perform *this action* on *this
specific record right now*? Two new attribute markers in `Core.Abstractions.Entities`:
`IHasOwner` (creator/assignee) and `IHasWorkflowState` (free-form lifecycle string —
the framework is workflow-*aware*, not an engine; state is a domain-owned attribute
policies read). In `Modulus.Platform` (`Authorization/Resources/`): `ResourceAttributes`
(normalized owner/org-unit/state snapshot, `From(object)` reads whichever markers a
record implements), `ResourceRequest` (pure eval input exposing composable condition
probes `OwnedByCaller`/`CallerHasPermission`/`InState`/`InCallerScope`), `AccessDecision`
(allow/deny + reason). Policies are **declarative rules-as-data**: `ResourcePolicy` +
fluent `ResourcePolicyBuilder` (`Allow`/`Deny`/`Transition(from→to)`), surfaced via
`Rules` for admin matrix review — no `switch` in handlers. `Evaluate` is
**deny-by-default with deny-override**, matching the grant resolver so the stack fails
closed consistently. Transitions are permissioned actions guarded by source state. The
enforcement point ships this increment: `IResourceAuthorizer`/`ResourceAuthorizer`
(scoped) builds the request from `ICurrentUser` (identity, not trusted claims) +
`ICurrentDataScope` (the `InCallerScope` probe **reuses increment 3**, so single-item
mirrors the bulk list filter — a record hidden in a list can't be opened by id).
`IResourcePolicyRegistry` maps type→policy (last wins); `AddResourcePolicy<T>(policy)`
registers it. Fail-closed: unregistered type or ungranted action denies; call
`authorizer.Authorize(record, "approve")` in a handler. Regression:
`ResourcePolicyTests` (10: owner-edits-draft, locked-after-submit, stranger denied,
edit-any override, approve needs perm+state, transition state-guard, deny-override on
archived, unknown action denied, anonymous never owner, rules exposed) +
`ResourceAuthorizerTests` (5: no-policy deny, owner-via-identity, non-owner deny,
anonymous deny, scope-gated read reusing `ICurrentDataScope`). **202 unit tests green**,
0 warnings / 0 errors.

#### P1-8. Field-level security — ✅ DONE (auth blueprint §22, increment 5)
Field-level security (blueprint §5.9, §11): two users who both legitimately open the
*same record* may be entitled to *different fields* of it — a recruiter sees the profile
but not the salary. Fields carry a **classification** declared on the model:
`FieldClassification` enum (Public < Internal < Confidential < Restricted) + a
`[Classified(...)]` attribute on properties (`Core.Abstractions.Entities`); unannotated =
Public. In `Modulus.Platform` (`Authorization/Fields/`): `FieldClassificationMap` reflects
a type's property→classification once and caches it (process-wide) — per-field checks
resolve from cache, never per-row reflection. `FieldSecurityProfile` + fluent
`FieldSecurityProfileBuilder` map classification (and per-field overrides) to read/write
`FieldRequirement` (Open | Permission | Closed), **deny-by-default**: a sensitive field
with no configured clearance is Closed, so *classifying a field on the model protects it
even before a profile exists*; Public is open; per-field override wins over the class
rule; read and write are configured independently (opening read never opens write).
Enforcement ships both boundaries the blueprint requires via `IFieldAuthorizer`/
`FieldAuthorizer` (scoped, resolves a per-request memoised `FieldMask` from
`ICurrentUser` permissions): `Redact<T>(dto)` masks unreadable fields on the read
projection (settable props reset to default — apply to the response DTO, not a tracked
entity), and `AuthorizeWrite(type, attemptedFields)` returns an `AccessDecision`
rejecting writes above the caller's clearance (unknown field names fail closed). A type
with no registered profile still resolves against `FieldSecurityProfile.Empty`, so
classification alone is fail-closed. `FieldMask.Fields` exposes resolved per-field access
for audit/matrix. `IFieldSecurityRegistry` maps type→profile (last wins);
`AddFieldSecurity<T>(profile)` registers it. Regression: `FieldSecurityProfileTests`
(8: public open, sensitive-unconfigured closed, class rule gates, read-only leaves write
closed, read/write independent, field override wins, partial override falls back, blank
permission rejected) + `FieldAuthorizerTests` (7: mask unreadable + keep public, preserve
when cleared, write rejects protected/allows public, write allows cleared field, unknown
field fail-closed, classified-without-profile fail-closed, mask exposed for audit).
**217 unit tests green**, 0 warnings / 0 errors.

#### P1-9. Feature entitlements — ✅ DONE (auth blueprint §22, increment 6)
Feature/entitlement gating (blueprint §5.11, §14): a dimension *above* per-user
permissions — is a capability *available to this tenant at all* (licensing, plan,
add-on, jurisdiction)? A feature disabled by entitlement is invisible to **everyone** in
the tenant, admins included; the gate sits *outside and before* the permission check.
Seam `IFeatureGate` (`Core.Abstractions`, `bool IsEnabled(feature)`) + `NullFeatureGate`
(everything on ⇒ no-op when feature mgmt off, mirrors `NullCurrentTenant`) +
`FeatureDisabledException`. In `Modulus.Platform` (`Authorization/Features/`): a
module-declared **catalog** (`FeatureDefinition` + `IFeatureCatalog`, parallel to
`IPermissionRegistry`, for admin discovery — not enforcement); the entitlement **store**
`IFeatureEntitlementStore`/`InMemoryFeatureEntitlementStore` (plans = named feature
bundles, tenant→plan assignment, per-tenant enable/disable overrides; runtime-mutable for
billing events); and the **resolver** `IFeatureEntitlementResolver`/
`FeatureEntitlementResolver` computing effective availability with hierarchical defaults
**plan → tenant override** (override wins both ways: add-on on / jurisdictional block
off), fail-closed — plus `EnabledFeatures(tenant)` for feature-aware menu building. The
enforcement point is the scoped `FeatureGate` bridge reading `ICurrentTenant`: host sees
every feature (deliberate all-tenant scope), a resolved tenant defers to its entitlements,
**no tenant resolved ⇒ fail-closed** — exactly mirroring the tenant query filter. Wired
as a PEP through the mediator: `[RequireFeature("…")]` + `FeatureGateBehavior` registered
**before** `AuthorizationBehavior` (availability decided ahead of capability), no-op under
`NullFeatureGate` until `AddFeatureGate` opts in. DI: `AddModulusAuthorization` TryAdds
catalog+store+resolver; `AddFeatures(...)` declares catalog entries; `AddFeatureEntitlements(seed)`
seeds plans/assignments; `AddFeatureGate()` turns on enforcement. Regression:
`FeatureEntitlementResolverTests` (8: plan grants, outside-plan fail-closed, enable add-on,
disable plan-feature, unknown tenant/feature, EnabledFeatures fold, runtime mutation) +
`FeatureGateTests` (3: resolved tenant, host all-features, no-tenant fail-closed) +
`FeatureGateBehaviorTests` (4: no-attr passthrough, disabled short-circuits before handler,
enabled proceeds, NullFeatureGate no-op). **232 unit tests green**, 0 warnings / 0 errors.

#### P1-10. Delegation + governance — ✅ DONE (auth blueprint §22, increment 7 — FINAL)
The last authorization layer (blueprint §5.13/§15 delegation, §5.6/§13 SoD, §5.14/§16
audit-governance), all in `Modulus.Platform` (`Authorization/Governance/`).
**Delegation / temporary access:** `Delegation` (from-user + roles snapshot → to-user, a
permission subset, a validity window, revoked flag; `IsActiveAt(now)` fail-closed) +
runtime-mutable `IDelegationStore`/`InMemoryDelegationStore` (`Delegate`/`Revoke`/`ActiveFor`).
`IDelegationResolver`/`DelegationResolver` computes a delegate's in-force permissions with
four non-negotiables: **expiry enforced at decision time** (via `TimeProvider`, never a
cleanup job), **revocation immediate**, **capped by the delegator's own DIRECT authority**
(re-resolved through the concrete `PermissionResolver` using the roles snapshot — "cannot
delegate what you do not have"), and **sub-delegation bounded** (the cap ignores the
delegator's *own* delegated authority, so delegated-through permissions are not
re-delegable). `DelegatedPermission` carries **on-behalf-of** provenance for audit. Made
**live** by `DelegationAwarePermissionResolver`, a decorator over `IPermissionResolver`
installed by `AddDelegation` — so delegated authority takes effect at
`ICurrentUser.HasPermission` with **zero change to the Identity checker**.
**Segregation of duties:** `SodConstraint` (a set of mutually-exclusive permissions,
"maker cannot be checker") + `ISodPolicy`/`SodPolicy` evaluating a principal's effective
set — a violation is holding ≥2 of a constraint's permissions — as an *analyzable standing
control* (run over any user for attestation, or over a proposed set to prevent creating a
toxic combination). **Effective-access reporting:** `IEffectiveAccessService`/
`EffectiveAccessService` composes direct (concrete resolver) + delegated (with provenance)
+ SoD violations into an `EffectiveAccessReport` — the "what could this user access?"
auditor/breach snapshot. **Recertification:** `RecertificationCampaign` expands access
snapshots into per-(user,permission) review lines (Direct/Delegated source), tracks
certify/revoke, exposes the revoked lines as actionable output, and completes when nothing
is pending. DI: base `AddModulusAuthorization` now registers the concrete `PermissionResolver`
once + maps the interface to it (so the decorator and reporter can depend on *direct*
resolution), and TryAdds `TimeProvider.System` + empty delegation/SoD defaults + the
reporter; `AddDelegation(seed?)` wires the store + resolver + `Replace`s `IPermissionResolver`
with the decorator; `AddSegregationOfDuties(constraints)` supersedes the empty SoD default.
Regression (+23): `DelegationResolverTests` (7), `DelegationAwarePermissionResolverTests`
(3), `SodPolicyTests` (6), `EffectiveAccessServiceTests` (3), `RecertificationCampaignTests`
(4). **255 unit tests green**, 0 warnings / 0 errors.

**Authorization framework blueprint delivery sequence (§22): COMPLETE — all 7 increments
shipped** (grant store → org scope → data/row scope → resource/workflow policy →
field-level security → feature entitlements → delegation + governance).

### 2026-07-19 review pass (P0s from the second architectural review)

#### R1. Trust & hygiene — ✅ DONE
- **`PROJECT_STRUCTURE.md` rewritten** — it described an unrelated project
  (FoodDeliveryApp, .NET 8, MediatR/MassTransit/Quartz); now documents the actual
  repo. Policy: never let a doc describe a codebase that isn't this one.
- **Phantom package pins pruned** from `Directory.Packages.props` (Hangfire, Quartz,
  Dapper, Cassandra, Cosmos, Elastic, DynamoDB, SQS, Service Bus, Serilog,
  BenchmarkDotNet, Bogus, unused OTel/Testcontainers/Rebus variants, JwtBearer, the
  never-resolved `Microsoft.IdentityModel.JsonWebKeys`). **Decision:** a pin exists
  only alongside the feature that uses it, or as a documented transitive/advisory
  pin (policy comment now at the top of the file). Kept: the M.E.* alignment group,
  Newtonsoft (transitive via Rebus), the IdentityModel advisory pins, SQLitePCLRaw.
- **`AddModulus(Action<ModulusBuilder>)` overload deleted** — it built a throwaway
  root provider while its comment claimed otherwise; no callers existed. Callers use
  the `IConfiguration`-taking overloads.
- Dropped the unused `map` parameter from `ModuleLoader.GetCombinedDependencies`.

#### R2. One error contract + strict binding — ✅ DONE (breaking, pre-1.0)
**Problem.** Two error shapes on the wire (`ApiErrorResponse` envelope vs RFC 7807
from the exception handler), and `EndpointDiscovery` binding silently swallowed
conversion failures — `GET /orders/not-a-guid` ran the handler against `Guid.Empty`;
a malformed JSON body wrote a 400 but **kept executing** the handler pipeline.

**Decision.** RFC 7807 is the only error contract. New internal
`ProblemResponses` helper (traceId always attached, independent of
`AddProblemDetails`); binding failures, validation failures,
`HttpResponseException`, and `SendErrorAsync` all emit it.
`ApiErrorResponse`/`ApiErrorDetail`/`ApiResponse.Fail` **removed** (the success
envelope `ApiResponse<T>` stays). Binding is strict: a matched property that fails
conversion → 400 validation-problem listing *all* bad parameters; bool tokens are
strict (`true/1/yes/on` / `false/0/no/off`, anything else 400); numeric/date parsing
is invariant-culture; malformed JSON short-circuits. Unknown route/query keys remain
ignored (tracking params aren't client errors). Regression: `EndpointBindingTests`
(7). The REPR engine's larger fate (custom dispatch vs minimal-API conventions) is a
separate decision — this fixes the correctness bugs regardless.

#### R3. EF-backed authorization stores — ✅ DONE (unblocks production authz)
New package **`Modulus.Authorization.EntityFrameworkCore`** (mirrors the
MultiTenancy EF package: references Platform + EFCore.Relational only).
`AuthorizationStoreDbContext` owns `ModulusPermissionGrants`, `ModulusOrgUnits` +
`ModulusOrgUnitParents` (DAG edges), `ModulusOrgPlacements`, `ModulusPlanFeatures` /
`ModulusTenantPlans` / `ModulusFeatureOverrides`, and `ModulusDelegations`; row
types are internal (small public surface). **Decisions:**
- Stores are **singletons over `IDbContextFactory`** (the seam interfaces are sync
  and consumed by singleton resolvers; per-request memoisation already happens at
  the scoped checker/scope layer). The context is registered only via the factory —
  never as `DbContext` — so it stays out of the module transaction fan-out;
  `MigrateAuthorizationStoreAsync` initialises the schema.
- `EfOrgHierarchy` serves closures from an in-memory snapshot (a rebuilt
  `InMemoryOrgHierarchy` — code reuse, identical DAG/cycle semantics) invalidated by
  local mutations and a 30s TTL (`CacheDuration`) for cross-instance convergence.
- Delegation role/permission sets are JSON columns; window validity is evaluated in
  memory via `Delegation.IsActiveAt` (SQLite stores DateTimeOffset as text — SQL
  comparison across offsets is unreliable).
- `AddEfCoreAuthorizationStores(configure)` is call-order independent
  (`RemoveAll` + re-register against the TryAdd defaults) and registers concrete
  store types for the async management APIs. In-memory seeds
  (`AddPermissionGrants`/`AddOrganization`/…) deliberately do **not** apply to EF
  stores — durable data is provisioned through the management APIs, not re-seeded
  per boot.
Regression: `EfAuthorizationStoreTests` (18, kept-open in-memory SQLite): both
registration orders supersede, grant/deny/revoke round-trips + upsert semantics,
durable DAG closures + reorg, placement mode replacement, plan redefinition +
overrides, delegation windows/revocation/JSON round-trip, fail-closed empties.

#### R4. Distributed idempotency store — ✅ DONE
New package **`Modulus.AspNetCore.Redis`** (it hosts Redis-backed *HTTP* concerns —
placing it in `Modulus.Caching.Redis` would force the AspNetCore framework
reference onto worker apps). `RedisIdempotencyStore` implements the existing
`IIdempotencyStore` protocol with two keys per idempotency key: a `:claim` string
written via **`SET NX`** (atomic first-caller-wins across nodes, holds the request
fingerprint) and a `:data` string with the completed response for replay; both
expire after `RetentionSeconds`. **Decisions:** replay wins over re-claiming even
if the claim key expired ahead of the data key; a corrupt stored entry is treated
as absent (re-claim + re-execute) rather than poisoning the key; a crashed node's
claim blocks retries until TTL — same fail-closed semantics as the in-memory
default. `AddRedisIdempotencyStore` reuses an existing `IConnectionMultiplexer` or
connects itself; call-order independent. Regression: `RedisIdempotencyStoreTests`
(8, substituted `IDatabase` — no server needed).

#### R5. Feature systems unified behind one decision — ✅ DONE
**Decision:** the two feature layers are *deliberate* and now composed, not
competing. Entitlements (`AddFeatureGate` / `IFeatureGate`) answer the
**commercial** question "may this tenant use the feature at all?"; FeatureManagement
flags (`AddModulusFeatureFlags` / `IFeatureManager`) answer the **operational**
question "is it currently rolled out?". `RequireFeature(...)` now enforces the
**conjunction** — entitled AND rolled out — consulting each layer only when its
system is registered (a bare container gates nothing; `NullFeatureGate` stays
allow-all until `AddFeatureGate`). The mediator `FeatureGateBehavior` remains
entitlement-only (Mediator cannot depend on FeatureManagement). Docs on both
extension classes explain the split. Regression: `RequireFeatureTests` (5).

#### R6. Authorization management (admin) API — ✅ DONE
New package **`Modulus.Authorization.Management`** (references
Authorization.EntityFrameworkCore + AspNetCore framework):
`MapModulusAuthorizationManagement()` maps a minimal-API group (default
`/authorization`) with grant CRUD (`/grants`), org structure (`/org/units`,
`/org/placements`), entitlements (`/features/plans`, `/features/tenants/{id}/plan`,
`/…/overrides`), and delegations (`/delegations` create/list/revoke). **Decisions:**
every endpoint requires the `authorization:manage` permission through the
framework's `:`-policy convention, declared via `AddModulusAuthorizationManagement()`
(which also calls `AddAuthorization()` — `AddModulusAuthorization` only registers
`AddAuthorizationCore`, and `UseAuthorization` demands the full registration);
enum-ish inputs travel as strings and invalid values return 400 validation
problems (the single RFC 7807 contract); handlers bind the *concrete* EF stores —
the management surface is explicitly coupled to the durable implementation.
`EfPermissionGrantStore` gained `GetGrantsForHolderAsync` (the admin/review read).
Regression: `AuthorizationManagementApiTests` (7, real TestServer + test auth
scheme: 401 unauthenticated, grant/org/entitlement/delegation flows, 400s).

#### Samples restore — ✅ DONE (2026-07-26)
`samples/` initially appeared to hold only an unrelated legacy ERP project
(`cobytemed-erp-app`, 330MB, zero references to any `Modulus.*` package) with
no build file; it was dogfooded via `modulus app Storefront --database SQLite`
plus `modulus migrate add InitialCreate` (an authored EF Core migration, not
just `EnsureCreated`) as the first sample. `samples/Storefront` is the literal
CLI output. Verified end to end — `dotnet build`/`test` 0 warnings/errors,
`dotnet run` serving real CRUD traffic against SQLite in both Development and
Production (`Migrate` mode) startup paths. **Bug found + fixed at the source
while dogfooding:** the CLI's `GetByIdHandler.sbn`/`UpdateHandler.sbn`/
`DeleteHandler.sbn` templates threw `InvalidOperationException` for a missing
entity instead of the framework's own `Modulus.Core.Abstractions.Exceptions.
NotFoundException` — every app scaffolded by `modulus app`/`add-module`/
`generate-crud` returned 500 instead of a 404 problem-details response for a
missing record. Fixed in the three `.sbn` templates (not just the generated
sample), so future scaffolds inherit the fix. `samples/Storefront/README.md`
documents what the sample demonstrates and how to run/test/extend it.

#### CobyteMed ERP retrofit onto Modulus — ✅ DONE (2026-07-26)
`cobytemed-erp-app` turned out to be the user's own real, pre-existing project
(a "Users module only" extract of a larger app) rather than disposable legacy
cruft, and was retrofitted onto Modulus in place rather than replaced — a
second sample showing incremental adoption into an opinionated, already-working
codebase, as opposed to Storefront's from-scratch CLI scaffold. Scope agreed
with the user up front: **core overlap only** (module system, mediator, HTTP
cross-cutting, observability — Rebus/Marten/Quartz/MinIO stay, Modulus has no
equivalent) and **keep `Result<T>`** (swap the mediator engine, not the app's
functional error-handling contract). Full details and the exact swap list are
in `samples/cobytemed-erp-app/README.md`; headline points:
- `UsersModule` → `ModulusModule` + `[DependsOn]`; `AddModulus<CobyteMedHostModule>`
  replaces the hand-called `AddUsersModule`.
- The app's `ICommand`/`ICommand<T>`/`IQuery<T>` marker interfaces now redirect
  onto `Modulus.Mediator.Abstractions`'s own (so none of the ~35 command/query
  record declarations needed to change); ~42 handler classes were re-targeted
  to `ICommandHandler<,>`/`IQueryHandler<,>` (`Handle` → `HandleAsync`); the
  app's own three pipeline behaviors were ported to Modulus's
  `IPipelineBehavior<,>` and registered **instead of** Modulus's built-ins
  (which throw on validation failure, breaking the `Result<T>` contract).
- `AddModulusCors`/`RateLimiting`/`SecurityHeaders`/`Correlation` replaced the
  app's own versions (two of which — CORS and rate-limiting middleware — were
  already dead code, never wired into the live pipeline); `AddModulusIdempotency`
  is net new. `Modulus.Observability` wires Mediator command/query spans into
  the app's existing OpenTelemetry pipeline.
- Forced a `net8.0` → `net10.0` bump (Modulus doesn't multi-target) and a
  matching wave of EF Core/Npgsql/FluentValidation/Swashbuckle package bumps,
  plus a local `Directory.Build.props`/`Directory.Packages.props` so the
  sample doesn't inherit the framework repo's own build props.
- Fixed several pre-existing, Modulus-unrelated defects blocking the build:
  a missing `using`, a dangling out-of-repo `Test.Shared` test reference
  (recreated locally, right-sized to actual usage), five integration tests
  exercising domain methods that don't exist on the trimmed `User` entity
  (removed rather than inventing behavior), an `Address.Create` argument-order
  bug, and a stale Dockerfile referencing removed modules.
- Verified: full solution builds 0 errors; 151 domain unit tests green.
  `IntegrationTests` compiles but needs a live Postgres (via Testcontainers,
  requires a running Docker daemon) to execute — not runnable in this sandbox,
  so it was verified by compilation only, not a live run.

**Follow-up pass (same day):** user asked to use the framework "where needed"
beyond core-overlap — re-surveyed every remaining non-Modulus piece against the
actual Modulus source rather than assumption. One genuine gap found and fixed:
`Microsoft.AspNetCore.Mvc.Versioning`/`.ApiExplorer` (a different, legacy
library family) were referenced but never wired to anything, despite every
route living under `/api/v1/...` — replaced with `AddModulusApiVersioning`
(`Asp.Versioning`-based, minimal-API-compatible). Everything else surveyed —
`ModuleDbContext` (would force a new outbox-table migration + entity
re-annotation for a capability the app already has), `Modulus.Data.PostgreSQL`
(gated by the same constraint), `Modulus.Sagas` (wraps Rebus generically but
only pays off by adopting Modulus's own `IIntegrationEvent`/`IOutboxDispatcher`
wholesale — a live-message-flow change unverifiable without a Docker daemon),
`Modulus.Testing` (hardcodes a SQLite swap incompatible with this app's
Postgres-specific JSONB/snake_case usage), `Modulus.AspNetCore.OpenApi` (no
ApiKey scheme support, a downgrade from Swashbuckle), `Modulus.Identity`'s
`AuthentikIdentityProvider` (no blacklist/session surface to replace what the
app already has), and `Modulus.Platform`'s caching/background-jobs (both a
capability downgrade from the app's existing Redis/Postgres/Valkey cache and
Quartz's cron-persistent jobs) — was confirmed to be either a genuine
non-overlap or a net capability loss, not a gap. Rechecked: full solution still
builds 0 errors, 151 unit tests still green.

#### Durable audit emission — increment 1 (administrative changes) — ✅ DONE (2026-07-26)
Blueprint §5.14/§16: "who was granted what, by whom, when" must be audited
*unconditionally*; allow/deny decision-auditing is a separate, *declaratively
scoped* concern (auditing every decision is "prohibitively voluminous") —
deferred as increment 2 (see below), so this pass covers administrative
changes only. New `Modulus.Authorization.Audit` namespace (`Modulus.Platform`):
`AuthorizationAdministrativeChangeEvent` (`IIntegrationEvent`, stable name
`authorization.administrative-change.v1`, Category/Action/ActorUserId/
TargetDescription/Details) + `IAuthorizationAuditWriter` seam, no-op
`NullAuthorizationAuditWriter` default (`AddModulusAuthorization` TryAdds it,
mirroring every other Null-default seam in the framework).
**`Modulus.Authorization.EntityFrameworkCore`** (`Audit/`): `AuthorizationStoreDbContext`
gained its own `OutboxMessage` table (`ModulusAuthorizationAuditOutbox`) —
**not** shared with a module's `outbox_messages`, since this context is
deliberately registered only via `IDbContextFactory` (R3's isolation decision)
and so can't ride `Modulus.Outbox`'s `OutboxProcessor`, which only scans
bare-registered `DbContext`s. `EfAuthorizationAuditWriter` persists immediately
(its own transaction — can't share the store write's transaction given that
isolation); `AuthorizationAuditRelayProcessor` (+ thin `AuthorizationAuditRelayService`
poller, split for testability the same way `OutboxProcessor`/`OutboxPollingService`
are) claims/dispatches/retries/dead-letters with the same guarantees as
`OutboxProcessor`, dispatching through whatever `IOutboxDispatcher` the host
already has registered. `ICurrentTenant`/`ICorrelationContext` are resolved
optionally (not TryAdd'd by this package) specifically to avoid a
registration-order race against a host's real multi-tenancy setup winning or
losing depending on call order. `AddEfCoreAuthorizationAudit(...)` wires it all
and supersedes the Null default. **`Modulus.Authorization.Management`**: all 12
mutating admin endpoints (grants ×2, org units ×2, org placements ×2, feature
entitlements ×4, delegations ×2) emit an audit event via a shared
`EmitAuditAsync` helper after their store write succeeds — never before, so a
failed/rejected request is never audited as if it happened.
`AddModulusAuthorizationManagement` also TryAdds `ICurrentUser` →
`NullCurrentUser` (previously only `AddModulus`/`AddMediator`/Identity did this,
so the package now resolves standalone). Regression: `AuthorizationAuditTests`
(9, kept-open in-memory SQLite — writer persistence, event-type
self-registration, dispatch/mark-processed, retry-with-backoff, dead-letter
after max retries, registration supersession) + `AuthorizationManagementAuditTests`
(5, real TestServer — a representative slice: grant grant/revoke, org-unit
create, delegation create are audited with the right Category/Action/Target;
a validation-rejected request is not). **All unit tests green** (114 in
`Modulus.Platform.Tests` alone), 0 warnings/errors solution-wide.

#### `ICurrentUser.Permissions` wired through the grant store — ✅ DONE (2026-07-26)
Increment-1 follow-up (auth blueprint §22): `HasPermission` already resolved
server-side via `GrantStorePermissionChecker`/`IPermissionResolver`, but the
sibling `Permissions` property on `ClaimsPrincipalCurrentUser` read raw
`"permission"` claims off the token unconditionally — a live grant-store
change (revoke, deny-override, delegation) would update what `HasPermission`
says but not what `Permissions` listed, and a stale/forged permission claim on
the token would leak straight into `Permissions` even with a checker
registered. **Root cause**: `IPermissionChecker` only exposed
`HasPermission(string)` — no set-returning member for `Permissions` to call,
even though `GrantStorePermissionChecker` already computed and cached the full
effective set internally (`_effective`) to answer `HasPermission`. **Fix**:
added `IPermissionChecker.GetEffectivePermissions() : IReadOnlyCollection<string>`
(a breaking interface change — pre-1.0, no compat shim per working agreement);
`GrantStorePermissionChecker` returns its already-cached `_effective` set;
`ClaimsPrincipalCurrentUser.Permissions` now calls it when a checker is
registered, falling back to token claims only when one isn't (preserving prior
behavior for apps that never opted into `AddGrantStorePermissionChecker`).
Delegation is automatically included with no extra code — `DelegationAwarePermissionResolver`
already implements the same `IPermissionResolver.Resolve` contract the checker
calls. Regression: `GrantStorePermissionCheckerTests` (+2:
`GetEffectivePermissions` full set, empty when unauthenticated) +
new `ClaimsPrincipalCurrentUserTests` (4: resolves from the grant store,
ignores a stale/forged permission claim once a checker is registered, empty
when unauthenticated, falls back to token claims with no checker) — the
codebase had zero prior test coverage on `ClaimsPrincipalCurrentUser` at all.
**Full solution: 0 warnings/errors, all unit tests green.**

#### Decision-audit emission — increment 2 — ✅ DONE (2026-07-26)
The scoped, opt-in half of blueprint §5.14/§16 deferred at the end of
increment 1: allow/deny decisions on `IResourceAuthorizer`/`IFieldAuthorizer`,
recording the deciding grant/rule — but only for resource types/actions
declaratively marked audit-worthy, since auditing every decision is
"prohibitively voluminous." **The sync/async question resolved itself**: a
repo-wide search turned up **zero real call sites** of either interface
anywhere in `src/` — both ship as enforcement points no handler in this
codebase calls yet, so converting `IResourceAuthorizer.Authorize` →
`AuthorizeAsync` and `IFieldAuthorizer.AuthorizeWrite` → `AuthorizeWriteAsync`
(both now `Task<AccessDecision>`, pre-1.0 breaking rename, no compat shim) had
a contained ripple: only the two tests exercising them needed `async Task` +
`await` (11 facts, mechanical). `Redact`/`MaskFor` (the read boundary) stay
synchronous — read-path field auditing is a further-scoped increment 3, out
of scope here (auditing every read touching a classified field is even higher
volume than write decisions). New `Modulus.Platform`
(`Authorization/Audit/`): `AccessDecisionAuditEvent` (stable name
`authorization.access-decision.v1`); `IAuditableActionRegistry` (declarative
`(Type, action)` opt-in, `NullAuditableActionRegistry` fail-safe default —
nothing audited until marked); `AuditingResourceAuthorizer`/
`AuditingFieldAuthorizer` decorators that pass the inner decision through
unchanged and emit an event only when the registry says so.
`AddScopedDecisionAuditing(configure)` wires it all, mirroring
`AddDelegation`'s `DelegationAwarePermissionResolver` pattern exactly
(register the concrete authorizer, `services.Replace` the public seam with
the decorator wrapping it). Regression: 9 new tests in
`ScopedDecisionAuditingTests` (registry marking, emits-when-marked/
emits-nothing-when-unmarked for both authorizers, `Redact` passthrough stays
unaudited, end-to-end DI wiring) + the 11 renamed-signature call sites fixed.
**Full solution: 0 warnings/errors, all unit tests green (123 in
`Modulus.Platform.Tests`).** Both audit-emission increments from the blueprint
are now shipped.

#### REPR engine's fate — ✅ DECIDED: keep REPR, simplify the engine (2026-07-26)
Zero real adopters of `Endpoint<TRequest,TResponse>` exist anywhere in the repo
or samples (confirmed by search) — the engineering case favored retiring it for
plain minimal APIs. **User's call: keep REPR as the authoring convention,
fix/simplify what's underneath it instead.** Sequenced as tests-first
(no test had ever driven a real `EndpointBase`-derived endpoint through
`MapModulusEndpoints` + real routing — `EndpointBindingTests` only calls the
internal `BindRequestAsync` helper against a hand-built `HttpContext`), then
simplify.

New `EndpointDispatchIntegrationTests` (11, real `WebApplication` +
`UseTestServer()`, mirroring `AuthorizationManagementApiTests`'s convention):
route/query binding through real `MapMethods` routing, malformed-route-value
400s, FluentValidation failures, malformed-JSON-body short-circuit,
`HttpResponseException`/`ThrowError` short-circuiting, `DontWrapResponse`,
role-based authorization enforcement (401/403/200). **This surfaced a real,
previously-unnoticed defect**: `MapCore`'s per-request delegate creates a
fresh `TEndpoint` instance via `ActivatorUtilities.CreateFactory` for every
request, but only the discovery-time throwaway instance ever had `Configure()`
called on it — the real per-request instance's own `Config` (read at runtime
by `SendOkAsync`/`SendCreatedAsync` for `WrapResponse`) was silently always
left at its untouched default. `DontWrapResponse()` was a no-op at runtime for
every endpoint that ever called it. **Fix**: `EndpointBase.Initialize` now
takes the discovery-time `EndpointConfig` and assigns it onto the per-request
instance (`internal void Initialize(HttpContext ctx, EndpointConfig config)`),
so `Configure()`'s effects reach the instance that actually serves the
request — no re-invocation of `Configure()` per request needed, consistent
with the "closed once at startup" design already documented on
`EndpointDiscovery`.

With the safety net in place, `TryConvertValue`'s bespoke per-type branches
(`Guid`/`DateTime`/`DateTimeOffset`/`TimeSpan` plus a `Convert.ChangeType`
catch-all) were replaced with a single path through `IParsable<T>.TryParse` —
the same convention ASP.NET Core's own minimal-API parameter binding uses —
cached per target `Type` via `MakeGenericMethod` (which doubles as the
"does this type support binding" check, since it fails the `where T :
IParsable<T>` constraint otherwise). The enum branch and the strict-bool
branch (R2's `true/1/yes/on` / `false/0/no/off` rule) are untouched — neither
maps cleanly onto `IParsable<T>` semantics. Authoring surface
(`EndpointBase`/`Configure()`/`HandleAsync()`) is unchanged.
**Full solution: 0 warnings/errors, all unit tests green (58 in
`Modulus.AspNetCore.Tests`, including the original 7 `EndpointBindingTests`
unchanged).**

#### Remaining P1 (not started)
Docs site; API baselines + package validation in CI; broader test coverage
push (regression test per fixed defect; fakes package).

### P2 / P3
Done since this list was written (verified in code): localization (`Modulus.Platform/Localization`),
audit (`Modulus.AuditLogging.EntityFrameworkCore`), persistent scheduler (`Modulus.BackgroundJobs.Quartz`),
distributed locking (`IDistributedLock`, Redis-backed), unified caching (FusionCache), architecture tests
(`Modulus.Testing.Architecture`).

Still open: convention DI registration + Decorate(), domain-event dispatch timing options, and framework
metrics (the Caching, Authorization, Tenancy and RateLimit meters are defined but never recorded). Done since: optimistic
concurrency conventions (`IHasConcurrencyStamp`) and pruning of unused package pins. AOT beyond the two packages that are
AOT-compatible was decided against; see AGENTS.md *Decided not to build*.

### Working agreements
- `TreatWarningsAsErrors` is global: every step must build with 0 warnings and pass
  `dotnet test modulus.slnx --filter "Category=Unit"`.
- Pre-1.0: breaking changes are allowed and preferred over compat shims, but wire
  formats (outbox rows, envelopes) get legacy fallbacks.
- No commits unless explicitly requested.

<!-- source: docs/archive/ROADMAP_TIER3.md -->
## Tier 3 Roadmap — Remaining API/Platform Robustness

> **Status: completed (archived 2026-10-11).** Every Tier 3 item is built. The unchecked boxes further down are the
> per-item template, not open work.

Implementation guide for the **remaining** Tier 3 items. Two are already done and
shipped (see `AGENTS.md` → "API robustness (Tier 3)"):

- ✅ **HTTP idempotency** — `Modulus.AspNetCore/Idempotency/`
- ✅ **OpenAPI hardening** — `Modulus.AspNetCore/OpenApi/`
- ✅ **Integration-test harness** — `src/testing/Modulus.Testing/` (see
  `AGENTS.md` → "API robustness (Tier 3)"; section 1 below is retained as the
  as-built record).
- ✅ **Feature flags** — `Modulus.AspNetCore/FeatureFlags/` (see `AGENTS.md`;
  section 2 below is retained as the as-built record).
- ✅ **Secrets management** — `Modulus.AspNetCore/Configuration/SecretsGuard*` (see
  `AGENTS.md`; section 3 below is retained as the as-built record).
- ✅ **PII encryption** — marker + `IPersonalDataProtector` in
  `Modulus.Core/Abstractions/DataProtection`; EF converter + model hook in
  `Modulus.EntityFrameworkCore/DataProtection`; DataProtection-backed impl +
  `AddModulusPersonalDataProtection` in `Modulus.AspNetCore/DataProtection` (see
  `AGENTS.md`; section 4 below is retained as the as-built record).

**All Tier 3 items are complete.** The sections below are retained as the as-built
records — follow the shared conventions when extending them.

---

### Shared conventions (read once)

Every cross-cutting feature in this framework follows the same shape. Match it.

- **Config-bound options.** One `sealed class XxxOptions` with `public const string
  SectionName = "Xxx";` and documented `{ get; set; }` properties with sane
  defaults. XML-doc every property (the repo runs `GenerateDocumentationFile` with
  `TreatWarningsAsErrors` — a missing/broken `<see cref>` fails the build).
- **Two extension methods.** `AddModulusXxx(this IServiceCollection, IConfiguration,
  Action<XxxOptions>? configure = null)` binds options + registers services;
  `UseModulusXxx(this IApplicationBuilder)` adds middleware (when applicable).
  Bind with `services.AddOptions<T>().Bind(section).ValidateOnStart()` (add
  `.ValidateOnStart()` only when you have validation).
- **Swappable services use `TryAdd`.** Register the default impl with
  `services.TryAddSingleton<IXxx, DefaultXxx>()` so a user who registered their own
  first wins. Document the "register yours before AddModulusXxx" rule.
- **Namespaces mirror folders**, `namespace X;` file-scoped, `using` directives
  *inside* the namespace (see any existing file). `ImplicitUsings` + `Nullable` are
  on.
- **Placement by dependency weight.** Zero-dependency / BCL-only abstractions →
  `Modulus.Core`. ASP.NET-shaped (HttpContext, middleware) → `Modulus.AspNetCore`.
  EF-shaped → `Modulus.EntityFrameworkCore`. Outbound platform concerns →
  `Modulus.Platform`. Don't add a package to Core.
- **Central Package Management.** New NuGet deps go in `Directory.Packages.props`
  (`<PackageVersion .../>`) with a one-line comment; reference them without a
  version in the `.csproj`. Keep the package count lean — justify every addition.
- **Tests** live in `tests/unit/Modulus.<Project>.Tests`. For `Modulus.AspNetCore`
  that project already exists; add `[Trait("Category", "Unit")]`, xUnit +
  FluentAssertions. Internal types are reachable via the `<InternalsVisibleTo>`
  already in the `.csproj`.
- **Template wiring.** Generated apps come from `cli/Templates/app/`. Add the
  `using`, the `AddModulusXxx(builder.Configuration)` call, any middleware line in
  `Program.sbn`, and the config block in `appsettings.json.sbn`. Keep middleware
  order deliberate and comment *why*.

#### End-to-end validation loop (run for every item)

The CLI (`modulus`) is a packed `dotnet tool`; templates are embedded, so template
changes require a **full reinstall** (same-version `tool update` no-ops).

```bash
# 1. clean build + tests
dotnet build modulus.slnx -c Debug            # expect 0 Warning(s) 0 Error(s)
dotnet test  tests/unit/Modulus.AspNetCore.Tests/Modulus.AspNetCore.Tests.csproj

# 2. pack as 1.0.0 (framework's default version is a prerelease; pack must be 1.0.0)
rm -rf nupkg
dotnet pack modulus.slnx -c Release -p:Version=1.0.0 -o nupkg

# 3. clear caches so the regen picks up fresh packages
dotnet nuget locals global-packages --clear
rm -rf ~/.nuget/packages/modulus.*

# 4. reinstall the CLI (uninstall first — same version won't upgrade)
dotnet tool uninstall --global Modulus.Cli
dotnet tool install  --global Modulus.Cli --add-source "$(pwd)/nupkg" --version 1.0.0

# 5. generate into the scratchpad and add a NuGet.config pointing at ./nupkg
#    (generated apps don't ship one), then build + run and probe the feature.
```

> Gotcha: the generated app has **no `NuGet.config`** — drop one in with a `local`
> source (`clear` + `local` → `./nupkg` + `nuget.org`) before `dotnet build`, or
> restore only sees nuget.org and fails on `Modulus.*` packages. Health endpoints
> are `/health/live` and `/health/ready` (not `/health`). OpenAPI is only mapped in
> `Development`.

---

### 1. Integration-test harness — ✅ DONE

> Implemented in `src/testing/Modulus.Testing/`. The spec below is the as-built
> record; a couple of decisions differ from the original sketch and are noted
> inline: the DbContext swap also removes the EF Core 9+
> `IDbContextOptionsConfiguration<TContext>` descriptor (not just
> `DbContextOptions<TContext>`), and isolation uses a unique `Cache=Shared` name
> rather than a single shared connection object.

**Goal.** Let module authors test real HTTP endpoints against a fully composed host
(all middleware, real mediator pipeline, a throwaway database) without hand-rolling
`WebApplicationFactory` boilerplate. This also becomes the framework's own
end-to-end safety net.

**Placement.** New **packable** library `src/testing/Modulus.Testing/` (it ships to
consumers, unlike the `tests/` projects). Add to `modulus.slnx` under a
`src` folder. `PackAsTool` = false; normal `<IsPackable>true</IsPackable>`.

**Dependencies (Directory.Packages.props):**
- `Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory<TEntryPoint>`.
- `Microsoft.EntityFrameworkCore.Sqlite` (already referenced elsewhere) for a
  file-less shared in-memory SQLite connection, OR reuse the EF InMemory provider.
  Prefer **SQLite in-memory** (`DataSource=:memory:` with an *open, held* connection)
  because it honors relational semantics the framework relies on (transactions,
  the outbox `ExecuteUpdate` claim). EF InMemory would give false greens.

**Public API.**

```csharp
// Base factory: boots the host, swaps every module DbContext to a shared
// open SQLite in-memory connection, runs EnsureCreated, exposes a client.
public class ModulusWebAppFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            // Replace each registered DbContextOptions with the shared connection.
            // Find DbContext registrations, remove their options, re-add UseSqlite(conn).
        });
    }

    // Helpers: CreateAuthenticatedClient(claims...), scope-per-request helpers,
    // a way to seed data via a module's IUnitOfWork, and tenant/correlation headers.
}
```

**Key implementation notes.**
- **Shared connection lifetime.** Open one `SqliteConnection("DataSource=:memory:")`
  and keep it open for the factory's lifetime; every DbContext must use *that same*
  connection or each gets its own empty database. Dispose it in `Dispose(bool)`.
- **Swapping DbContexts generically.** Modules register their own
  `DbContextOptions<TModuleDbContext>`. Enumerate the `IServiceCollection` for
  `DbContextOptions<>` descriptors, remove them, and re-register with
  `UseSqlite(sharedConnection)`. A small reflection helper over the closed generic
  keeps it module-agnostic.
- **Migrations vs EnsureCreated.** In tests call `EnsureCreated` per module (the
  same fallback `MigrateModulusDatabasesAsync` uses when no migrations exist) so
  the harness works before authors write migrations.
- **Auth.** Provide a `TestAuthHandler` (`AuthenticationHandler<>`) registered as a
  scheme so `CreateAuthenticatedClient(userId, tenantId, roles…)` yields a principal
  without OpenIddict. Wire `ICurrentUser`/`ICurrentTenant` from it.
- **Determinism.** Disable rate limiting and set a fixed `TimeProvider` where tests
  need it (register `TimeProvider` in `ConfigureTestServices`).

**Template wiring.** Generated solutions already scaffold a test project (see
`cli/Templates`). Add a template that references `Modulus.Testing` and ships one
smoke test: `POST` an entity, `GET` it back, assert 200 — proving the harness boots
the generated host. Add `Modulus.Testing` to the generated test `.csproj` template.

**Tests (for the harness itself).** A tiny fixture host in
`tests/unit/Modulus.Testing.Tests` (or an integration project) that boots
`ModulusWebAppFactory<FakeProgram>` with one in-memory module and asserts: DB is
isolated per factory instance; authenticated client carries the principal; a second
factory doesn't see the first's data.

**Validation.** Run the shipped smoke test in a regenerated app; confirm it passes
against the real host (all middleware active, idempotency + correlation in the
pipeline).

**Effort:** M–L. **Risk:** the generic DbContext-swap reflection; test it directly.

---

### 2. Feature flags — ✅ DONE

> Implemented in `src/core/Modulus.AspNetCore/FeatureFlags/`. As-built notes vs the
> sketch below: evaluation is **scoped** (`AddScopedFeatureManagement`, so filters
> can read tenant/user), not the default singleton; there is no `IModulusFeatures`
> façade (expose `IFeatureManager` directly — it didn't earn its keep); and the
> minimal-API gate is `.RequireFeature("Flag")` (`RequireFeatureExtensions`) which
> short-circuits with **404** rather than a 403/short-circuit, to hide the endpoint.
> `SectionName` lives as a `const` on `FeatureFlagsExtensions` (no fixed
> `XxxOptions` class — the config *is* the dynamic flag set).

**Goal.** Runtime on/off (and gradual rollout) for behavior, bindable from config
and swappable to a provider (e.g. a database or a service) later.

**Placement.** `src/core/Modulus.AspNetCore/FeatureFlags/`.

**Dependencies.** `Microsoft.FeatureManagement.AspNetCore` (add to
`Directory.Packages.props`). It gives config-bound flags, percentage/time-window
filters, and `[FeatureGate]` on controllers/actions out of the box — don't
reinvent it. Wrap it thinly so the framework owns the entry point and defaults.

**Public API.**

```csharp
public static IServiceCollection AddModulusFeatureFlags(
    this IServiceCollection services, IConfiguration configuration);
// Registers FeatureManagement bound to the "FeatureManagement" section,
// adds the built-in filters (Percentage, TimeWindow), and exposes
// IFeatureManager / IVariantFeatureManager.
```

Optionally a small `IModulusFeatures` façade over `IFeatureManager` so call sites
depend on a framework interface, not the third-party one — but only if it earns its
keep; otherwise expose `IFeatureManager` directly.

**Key notes.**
- Config section is `FeatureManagement` (the library's convention — keep it so
  operators can use standard docs). Example seed in `appsettings.json.sbn`:
  ```json
  "FeatureManagement": { "SampleFeature": false }
  ```
- For gating minimal-API/REPR endpoints (the framework's endpoint style), add a
  small filter or an `.RequireFeature("X")` endpoint-convention helper — MVC gets
  `[FeatureGate]` for free.
- Keep evaluation **scoped** (per-request) so filters can read tenant/user context.

**Template wiring.** `using Microsoft.FeatureManagement;` +
`AddModulusFeatureFlags(builder.Configuration)` in `Program.sbn`; a `FeatureManagement`
block in `appsettings.json.sbn` with one disabled sample flag.

**Tests.** Bind an in-memory config with a flag on/off and assert
`IFeatureManager.IsEnabledAsync` reflects it; assert a percentage filter is
registered. (Most logic is the library's — test the *wiring*, not the library.)

**Validation.** In a regenerated app, add a sample gated endpoint, flip the flag in
config, confirm 200 vs 404/short-circuit.

**Effort:** S. **Risk:** low.

---

### 3. Secrets management — ✅ DONE

> Implemented in `src/core/Modulus.AspNetCore/Configuration/` as
> `SecretsGuardOptions` / `SecretsGuardScanner` / `SecretsGuardHostedService` /
> `SecretsGuardExtensions`. As-built notes vs the sketch below: the guard runs as an
> `IHostedService` (fail-fast in `StartAsync`) rather than an inline check, so it
> scans the fully-composed configuration; detection is factored into a pure static
> `SecretsGuardScanner` for unit testing. A committed value is identified by finding
> the **effective** provider per key (last-wins over `IConfigurationRoot.Providers`)
> and checking it is a `FileConfigurationProvider` whose physical file sits under the
> content root — this is what distinguishes committed `appsettings*.json` from User
> Secrets (out-of-tree) and env/vault providers (not file-backed). To avoid false
> positives that would block a boot, connection strings are flagged only when they
> carry a credential (`Password=`/`Pwd=`/`AccountKey=`/`SharedAccessKey=`) **and**
> don't target a local host (`localhost`/`127.0.0.1`/`(local)`/`(localdb)`), so a
> SQLite or localhost dev string is ignored; non-connection-string secret-shaped keys
> are flagged on any non-empty committed value. Defaults to Development + Staging.
> No `Modulus.Platform` Key Vault packages were added (left as documented opt-in).
> Template hygiene shipped: host `.csproj` `<UserSecretsId>` (per-app GUID from the
> model), a generated `.gitignore` (`app/gitignore.sbn`) covering `secrets.json` /
> `appsettings.*.json` / `*.db`, and a `SecretsGuard` block in `appsettings.json`.
> 7 scanner unit tests; validated end-to-end (fake `ExternalApi:ApiKey` in
> `appsettings.json` → startup fails in Development; same key via env var → boots).

**Goal.** Make it hard to commit secrets and easy to source them correctly per
environment (User Secrets in dev, environment variables / a vault in prod). This is
**mostly guidance + guard rails**, not a big code feature — resist over-building.

**Placement.** Guidance in docs + a thin optional helper in `Modulus.AspNetCore`
(or `Modulus.Core` config helpers). No new hard dependency by default.

**What to actually build.**
- **A config-validation guard rail.** An `AddModulusSecretsGuard()` (dev/staging
  only) that scans the *effective* configuration at startup for connection strings
  / keys that still hold obvious placeholders or look like real secrets checked into
  `appsettings.json` (not from env/UserSecrets/KeyVault providers), and **fails
  fast** (or warns) with a clear message. Implement by inspecting
  `IConfigurationRoot.Providers` and flagging sensitive keys whose value originates
  from the JSON file provider. Keep the sensitive-key list configurable
  (`ConnectionStrings:*`, `*Secret`, `*Password`, `*ApiKey`, `*Token`).
- **Provider wiring guidance** (docs): enable User Secrets in the template for
  Development (`builder.Configuration.AddUserSecrets<>()` guarded by
  `IsDevelopment()`); document env-var and Azure Key Vault / AWS Secrets Manager
  provider registration as opt-in snippets (do **not** take those SDK deps by
  default).
- **Template hygiene.** Ensure `appsettings.json.sbn` ships **no real secrets** —
  connection strings use obvious local/dev placeholders; add a
  `appsettings.Development.json` note and confirm `.gitignore` covers
  `*.Development.json` secrets and `secrets.json`. (Also honors the repo rule: never
  commit connection strings/secrets.)

**Optional (behind opt-in packages):** first-class Key Vault / Secrets Manager
extension methods in `Modulus.Platform` (`AddModulusAzureKeyVault(...)`), each in
its own package so the core stays dep-free.

**Tests.** The guard rail is unit-testable: build a `ConfigurationRoot` from a JSON
provider + an env provider, assert the guard flags a JSON-sourced
`ConnectionStrings:X` but ignores the same key when it comes from env.

**Validation.** Regenerated app: put a fake secret in `appsettings.json`, confirm
the guard fails startup in Development; move it to env, confirm it boots.

**Effort:** S–M. **Risk:** low; keep it advisory to avoid false-positive boot
failures in prod (default the guard to Development only).

---

### 4. PII encryption — ✅ DONE

> Implemented across three assemblies, keeping `Modulus.Core` dep-free and adding **no
> new NuGet dependency** (Data Protection comes from the ASP.NET shared framework):
> - `Modulus.Core/Abstractions/DataProtection/` — `ProtectedPersonalDataAttribute`
>   marker and the `IPersonalDataProtector` abstraction (`Protect`/`Unprotect`/`Hash`).
> - `Modulus.EntityFrameworkCore/DataProtection/` — `EncryptingConverter`
>   (`ValueConverter<string,string>` wrapping the protector) and the public
>   `ModelBuilder.UseModulusPersonalDataEncryption(protector)` hook, which walks the
>   model and attaches the converter to every marked string property. The base
>   `ModuleDbContext.OnModelCreating` calls it automatically **only when an
>   `IPersonalDataProtector` is resolvable** (`sp.GetService<…>()`), so encryption is
>   opt-in and zero-cost when unused.
> - `Modulus.AspNetCore/DataProtection/` — `DataProtectionPersonalDataProtector`
>   (backed by `IDataProtector` with a stable `Purpose`; HMAC-SHA256 `Hash` keyed by
>   `SearchHashKey`), `PersonalDataProtectionOptions`, and
>   `AddModulusPersonalDataProtection(configuration)` which honors `Enabled`
>   (false ⇒ registers nothing), calls `AddDataProtection()`, and `TryAdd`s the default
>   protector so a user can register their own first.
>
> As-built decisions vs the sketch below: the DataProtection-backed impl lives in
> `Modulus.AspNetCore` (which already has the ASP.NET shared framework) rather than
> forcing a `Microsoft.AspNetCore.DataProtection` package into the EF library; the EF
> library depends only on the `IPersonalDataProtector` abstraction. The search-hash key
> is **required** for `Hash` (it throws with a clear message when unset) and is never
> seeded into `appsettings.json` — supply it via User Secrets / env. The sample entity
> ships **unencrypted**; marking a field is the opt-in. Tests: 4 in
> `Modulus.EntityFrameworkCore.Tests` (real SQLite: ciphertext at rest, plaintext in
> memory, unmarked columns untouched, deterministic-hash equality search) using a
> reversible fake protector, and 5 in `Modulus.AspNetCore.Tests` exercising the real
> Data Protection impl (round-trip, non-deterministic ciphertext, keyed hash,
> throws-without-key, persisted key ring decrypts an earlier provider's ciphertext).
> Validated end-to-end: a regenerated app with `[ProtectedPersonalData]` on the sample
> `Name` stores `CfDJ8…` ciphertext in `catalog.db` (no plaintext in the file) and reads
> the value back over HTTP transparently.

**Goal.** Encrypt designated personal-data columns at rest, transparently, with a
managed key that can rotate. Largest item — plan carefully.

**Placement.** Abstraction (key provider + the marker) in `Modulus.Core`; EF
integration (value converters, model hook) in `Modulus.EntityFrameworkCore`.

**Design.**
- **Marker.** `[ProtectedPersonalData]` attribute (or reuse ASP.NET Identity's
  `[ProtectedPersonalData]` concept) on string properties to encrypt. A
  convention-based model hook applies a `ValueConverter` to every marked property.
- **Key provider.** `IDataProtectionKeyProvider` / or lean on
  **`Microsoft.AspNetCore.DataProtection`** (`IDataProtector` with a named purpose).
  DataProtection already handles key storage, rotation, and ring management — prefer
  it over hand-rolled AES. Add `Microsoft.AspNetCore.DataProtection` (already in the
  ASP.NET shared framework for the API; for a pure EF library add the package).
- **Value converter.** `EncryptingConverter : ValueConverter<string, string>`
  wrapping `protector.Protect` / `Unprotect`. Apply in each module DbContext's
  `OnModelCreating` via a shared
  `modelBuilder.UseModulusPersonalDataEncryption(protector)` extension that walks
  entity types and applies the converter to `[ProtectedPersonalData]` properties.
- **Searchability caveat.** Encrypted columns can't be queried by equality. For
  fields that must be looked up (e.g. email), add an accompanying **deterministic
  HMAC hash** column (`XxxHash`) for equality search while the value itself stays
  encrypted. Document this explicitly.

**Key management notes.**
- Persist the DataProtection key ring outside the app (file share, DB, Key Vault) in
  production — document it; the in-memory default loses data across restarts.
- Rotation: DataProtection decrypts with old keys and encrypts with the newest, so
  reads keep working after rotation without a bulk re-encrypt. Note that changing
  the *protector purpose string* breaks decryption — keep it stable.
- Migrations: enabling encryption on an existing column requires a one-off
  data-migration pass (read plaintext, write ciphertext) — provide a documented
  `modulus` maintenance command or a sample migration, don't silently corrupt data.

**Public API.**

```csharp
// registration
services.AddModulusPersonalDataProtection(configuration); // sets up IDataProtector purpose

// in a module DbContext
protected override void OnModelCreating(ModelBuilder b)
    => b.UseModulusPersonalDataEncryption(); // applies converter to [ProtectedPersonalData]
```

**Tests.** Round-trip a `[ProtectedPersonalData]` entity through an in-memory SQLite
DbContext: raw column value in the DB is ciphertext (assert it != plaintext), the
materialized entity is plaintext; the HMAC hash column matches for equality lookup;
rotation (new key added) still decrypts old rows.

**Validation.** Regenerated app with a module having an encrypted field: create an
entity, inspect the SQLite file/row to confirm ciphertext at rest, read it back via
the API to confirm transparent decryption.

**Effort:** L. **Risk:** M–H — key management and the existing-data migration story
are where correctness bugs hide. Treat the hash-for-search and rotation cases as
first-class tests, not afterthoughts.

---

### Definition of done (per item)

- [ ] Options + `AddModulusXxx` / `UseModulusXxx` following the shared conventions.
- [ ] New deps (if any) in `Directory.Packages.props` with a justification comment.
- [ ] Template wired (`Program.sbn` + `appsettings.json.sbn`), middleware order
      commented.
- [ ] Unit tests in `tests/unit/Modulus.<Project>.Tests` (added to `modulus.slnx`
      if a new project).
- [ ] `dotnet build modulus.slnx -c Debug` → **0 Warning(s), 0 Error(s)**.
- [ ] End-to-end: pack 1.0.0 → reinstall CLI → regenerate → build 0/0 → run → probe
      the feature over HTTP.
- [ ] `AGENTS.md` "API robustness (Tier 3)" section extended.
- [ ] Nothing committed unless explicitly asked; no secrets/`bin`/`obj`/connection
      strings staged.

<!-- source: docs/security/database-roles.md -->
## Database roles for tenant isolation

Database-enforced company isolation (security plan, phase 3) only holds when the application connects as a
role that the database actually restricts. Use **two** roles per database:

| Role | Used by | Owns the schema | Subject to row-level security |
|---|---|---|---|
| Migration role | `modulus migrate update`, the migrator job / init container, `*RowLevelSecurity.EnsureAsync` | yes | no (needs to create policies) |
| Application role | the running app (`ConnectionStrings:*`) | no | yes |

Never run the app with the migration role: PostgreSQL skips row-level security for the table owner unless the
policy is `FORCE`d (Modulus forces it) and always for superusers and `BYPASSRLS` roles; on SQL Server an owner
can drop the security policy.

### PostgreSQL

```sql
-- Run as an administrator once per database.
CREATE ROLE app_migrator LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;
CREATE ROLE app_runtime  LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;

GRANT CONNECT ON DATABASE app TO app_migrator, app_runtime;
GRANT USAGE, CREATE ON SCHEMA public TO app_migrator;
GRANT USAGE ON SCHEMA public TO app_runtime;

-- Tables created later by the migrator are usable by the app, without ownership.
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime;
```

Policies: `migrationBuilder.EnableTenantRowLevelSecurity("cat_products")` in a migration, or
`await PostgreSqlRowLevelSecurity.EnsureAsync(context)` as the migrator after migrating. The app registers
`services.AddPostgreSqlRowLevelSecurity<CatalogDbContext>()` next to `AddPostgreSQLDatabase<CatalogDbContext>(...)`.

Check: `SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'app_runtime';` must show `f, f`.

### SQL Server

```sql
-- Server level
CREATE LOGIN app_migrator WITH PASSWORD = '...';
CREATE LOGIN app_runtime  WITH PASSWORD = '...';

-- In the application database
CREATE USER app_migrator FOR LOGIN app_migrator;
CREATE USER app_runtime  FOR LOGIN app_runtime;
ALTER ROLE db_ddladmin ADD MEMBER app_migrator;
GRANT ALTER ANY SECURITY POLICY, ALTER ANY SCHEMA, REFERENCES TO app_migrator;

ALTER ROLE db_datareader ADD MEMBER app_runtime;
ALTER ROLE db_datawriter ADD MEMBER app_runtime;
-- app_runtime gets no db_owner, no db_ddladmin, no ALTER ANY SECURITY POLICY, no CONTROL on schema [modulus].
```

Policies: `migrationBuilder.EnableTenantRowLevelSecurity("cat_Products")` in a migration, or
`await SqlServerRowLevelSecurity.EnsureAsync(context)` as the migrator. The app registers
`services.AddSqlServerRowLevelSecurity<CatalogDbContext>()`.

### MySQL

MySQL has no row-level security. Prefer one database per tenant (`AddMySQLPerTenantDatabase`), each reachable
only by the app account:

```sql
CREATE USER 'app_migrator'@'%' IDENTIFIED BY '...';
CREATE USER 'app_runtime'@'%'  IDENTIFIED BY '...';
-- Per tenant database (t_<tenant id without dashes>) and the host database:
GRANT ALL PRIVILEGES ON `t\_%`.* TO 'app_migrator'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON `t\_%`.* TO 'app_runtime'@'%';
```

With a shared MySQL database the EF filter, the write guard and the raw-SQL guard are the only controls; the
startup check (`Security:DataIsolation:Tier: Shared`) logs that residual risk.

### SQLite

Development only for shared files. For tenant data use one file per tenant (`AddSQLitePerTenantDatabase`); the
startup check refuses a shared SQLite file outside Development when `Security:DataIsolation:Tier` is set.

### The security audit table

`modulus_security_audit` (the hash-chained security log) must be append-only for the application. The
`AuditStoreExtensions` helpers return the statements to run as the migrator after the table is created:

- `PostgreSql(applicationRole, schema)`: the role gets `SELECT, INSERT` only, and a trigger refuses `UPDATE`, `DELETE`
  and `TRUNCATE`.
- `SqlServer(applicationUser, schema)`: `GRANT SELECT, INSERT`, `DENY UPDATE, DELETE`, and an `INSTEAD OF` trigger that throws.
- `MySql(applicationUser, database, host)`: `GRANT SELECT, INSERT`, and `BEFORE UPDATE` / `BEFORE DELETE` triggers that signal an error.

The append-only rule is a second line of defence. The chain verification (`VerifyChainAsync`) is what detects an edit
that gets through, so run it on a schedule and keep the anchor file somewhere the application cannot write.

<!-- source: docs/security/policy-combination.md -->
## Authorization policy combination

How Modulus combines the layers of an authorization decision, and what wins when they disagree. Every rule here is
deterministic and covered by a test (named in brackets). When a layer cannot be evaluated, the answer is **deny**.

### The layers, in the order they are applied

| # | Layer | Question | Where |
|---|-------|----------|-------|
| 1 | Identity | Is there a verified principal? | authentication scheme, `ICurrentUser` |
| 2 | Tenant (company) | Is the caller a member of the selected company? Is it active? | `TenantMiddleware`, `RequireMembership` |
| 3 | Endpoint policy | Does the endpoint declare how it is protected? | security guard, `Loosen(reason)` |
| 4 | Permission | Does the caller hold the permission? | `PermissionResolver` |
| 5 | Scope | Over which data does the permission apply? | `PermissionScopeResolver`, `IScopeEnforcer` |
| 6 | Resource rules | Is this record, in this state, allowed for this action? | `ResourcePolicy` |
| 7 | Fields | Which fields may the caller see or set? | `IFieldAuthorizer` |

A request is allowed only if **every applicable layer allows it**. A broad grant at one layer never bypasses a narrower
requirement at another: a tenant-wide `inventory:stock:view` does not open a record the resource rules refuse, and a
permission does not cross a company boundary.

### Rules inside the permission layer

1. **Deny wins.** An explicit `Deny` (role or user, wildcard included) removes the permission, whatever allows exist, and
   whatever scope they carry. [`RoleDeny_OverridesRoleAllow`, `UserDeny_OverridesRoleAllow`, `An_explicit_deny_wins_over_every_scope`]
2. **Deny applies after implication.** An allow confers what the permission `Requires`; a deny on the implied
   permission still removes it. [`Deny_IsAppliedAfterImplicationClosure`]
3. **Allows union, restrictions intersect.** Several allows add up (`Own` ∪ `Assigned` ∪ ...). A `Restrict` grant narrows
   every alternative and never makes a permission effective by itself. [`Scopes_from_several_grants_union`,
   `A_restriction_narrows_a_wider_grant`, `A_restriction_alone_grants_nothing`]
4. **Wildcards stop short of Critical.** `module:group:*` as an allow never confers a `Critical` permission; as a deny it
   removes them. [`PermissionSensitivityTests`]
5. **Time is decided at the decision.** A grant outside `ValidFrom`/`ValidUntil` does not apply; no cleanup job is needed.
   [`A_grant_stops_applying_when_it_expires_with_no_administrator_action`]
6. **Unknown or unregistered is nothing.** An unknown permission, an unknown wildcard prefix and an anonymous caller resolve to
   no access. [`NoGrants_ResolvesToEmpty_FailClosed`, `UnknownWildcardPrefix_ExpandsToNothing_FailClosed`]
7. **Delegation lends, it does not add.** A delegate receives the delegator's own scope for the delegated permissions, capped at
   what the delegator holds, never re-delegated, and a deny on the delegate wins over it. [`A_delegate_gets_the_delegators_scope_never_more`,
   `A_deny_on_the_delegate_wins_over_a_delegation`]

### Rules inside the resource layer

1. Rules are evaluated **deny-by-default with deny-override**: a satisfied `Deny` rule refuses the action; otherwise some
   satisfied `Allow` rule must match; no match refuses. [`ResourcePolicy`]
2. A rule that throws counts as not matched and refuses with `EVALUATION_ERROR`; it never propagates as permission.
3. A type with no policy gets `METADATA_MISSING` (deny).
4. Separation of duties (`SOD_CONFLICT`) is a resource-layer denial and is checked when the action runs, not when a screen renders.

### Mandatory versus ordinary

* **Mandatory (cannot be overridden by a role or grant):** a missing or unverified identity, a suspended or unknown company,
  a revoked membership, the cross-tenant write guard, a failed endpoint policy, an explicit deny, and the Critical-permission
  wildcard rule.
* **Ordinary (granted and revoked through roles, grants, scopes, delegations):** everything else.
* A mandatory denial is never turned into a permit by a more specific ordinary grant. Break-glass access to a company
  (`HostTenantAccessPolicy`) is itself an audited, separately authorized decision, not a role.

### Lists, single records and exports must agree

The same scope that filters a list (`IScopeEnforcer.Apply`) is the one that checks a single record. A new route that returns
the same data (by id, export, report, bulk action) must go through that scope too. [`Lists_and_single_record_checks_agree_for_every_scope_shape`]

### Revocation windows

Decisions are computed per request from the grant store; nothing about grants is cached across requests
(`CachedPermissionGrantStore` memoizes within one request only). So a removed grant, deny, scope, assignment, membership or
approval limit applies on the **next request**, on every node. The places where state is held longer, and for how long:

| State | Held for | How to tighten |
|-------|----------|----------------|
| Grants, scopes, assignments, memberships, approval limits | one request | n/a |
| Delegation snapshot of the delegator's roles | `DelegationRoleRefreshOptions.Interval` (1 min), plus an immediate refresh through `IAccessChangeObserver` on the node that made the change | lower the interval |
| Org hierarchy snapshot (`EfOrgHierarchy`) | 30 s per node; the node that edits it drops its own snapshot at once, other nodes wait for expiry | lower `CacheDuration` |
| `permission` claims inside a token | until the token expires | `TrustPermissionClaims = false` makes the grant store the only source |
| Access token of a disabled or deleted account | until it expires, unless `Identity:ValidateTokenEntries` (default `true`) checks the stored token entry, which revocation removes | keep it on |
| The AI platform's cached access scope | up to 5 min, or until the connector's revocation signal is delivered (`IAccessChangeObserver`; durable with `RevocationSpoolFile`) | register observers for new code that changes access |

Rule for new code: anything that changes grants, roles, memberships, accounts or limits calls
`NotifyAccessChangedAsync`. A mandatory dependency that cannot be read fails the request; it never falls back to a stale grant.

### Authority at the moment of the action (time of check, time of use)

A screen that showed an Approve button minutes ago proves nothing at the moment of posting. In every sensitive handler:

1. load the record inside the command's transaction (the mediator's `[Transactional]` wraps it);
2. `await authorizer.EnsureAllowedAsync(record, "approve")` (current permissions, scope, state, limits, separation of duties; `403` with the
   reason code when refused);
3. change the record and save with its concurrency stamp (`IHasConcurrencyStamp`; a concurrent change is a `409`), and persist who acted
   (`IHasApprovalTrail`) so the next step's separation-of-duties check has it.

Two requests racing for the same document therefore cannot both pass: the second sees the new state or fails the stamp.

### Explaining a decision

`ResourcePolicy.Explain`, `ResourceAuthorizer.Explain` and the `/authorization/resources/{type}/{id}/explain` endpoint list
which rules matched, for administrators; `AccessReasonCodes` names the outcome in audit events and API errors.

### Policy versions and decision replay

Requirements are compiled code, so a past decision cannot be re-executed later; what Modulus keeps is enough to tell whether it *could* be reproduced.
`ResourcePolicy.Define(p => p.Version("2026-10").Allow(...))` labels a revision (bump it whenever a requirement changes), and `ResourcePolicy.Fingerprint`
hashes the version plus every rule's position, effect, action and target state. An audited resource decision (`AccessDecisionAuditEvent`) records both
(`PolicyVersion`, `PolicyFingerprint`), and `PolicyFingerprints.IsCurrent(decision, registry, type)` answers whether the policy in force is still the one that
decided. `false` means the rules changed (or are gone): read the decision as a record, and use the version label to find the code revision that produced it.
Not built: storing per-rule evidence with each decision, or a policy store that keeps old revisions executable.

### Step-up, machine principals and sessions

- **Step-up** (`StepUpPolicy`, `ModulusAuthorizationOptions.StepUp`). A permission that needs step-up is held only with a
  fresh multi-factor sign-in (`amr` contains `mfa`, `auth_time` within `MaxAge`). The check runs in every path: the HTTP
  permission handler, `AuthorizationBehavior` (mediator, through `IStepUpGate`) and resource rules (`ResourceAuthorizer`).
  A principal is exempt only with the `modulus:machine` claim and an `integration:` role; a person given that role still
  steps up. Outside a request there is no principal, so the permission is refused. [`StepUpTests`, `AuthorizationBehaviorTests`,
  `ResourceAuthorizerTests`]
- **Sessions.** A token whose session has ended is rejected at once when the session registry is on (`SessionValidationHandler`);
  sign-out, the concurrency cap and the idle timeout all end sessions. [`SessionHttpTests`]
- **Party ceilings.** An external-party account keeps only the grants its kind's ceiling covers; a deny always survives.
  Scopes of kind `assigned:{kind}` see only that party. (No dedicated test yet; the ceilings are stored by `EfPartyStore`.)
- **Machine clients** are granted through the same store as people (`integration:{clientId}` role), so removing a grant stops
  them on the next request.

---

<!-- in-progress plan; canonical copy: docs/IDENTITY_PLATFORM_IMPLEMENTATION_PLAN.md -->
## Identity Platform Implementation Plan (BRS, baseline v1.3.1 source-verified) — registration, status in IDENTITY_PLATFORM_PLAN.md

Turns the *Identity, Authentication & Authorization Platform BRS* (ERP-agnostic; first consumer
Garments ERP) into implementation work on Modulus while keeping the framework generic. The canonical
living plan is [`docs/IDENTITY_PLATFORM_IMPLEMENTATION_PLAN.md`](IDENTITY_PLATFORM_IMPLEMENTATION_PLAN.md)
(**v1.3.1** — the approved v1.3 architecture baseline reconciled with a source-level audit of this
repository, 2026-10-15; every current-state claim carries file:line evidence). The BRS §16.3 truth-table
conformance and traceability specification lives in
[`docs/AUTHORIZATION_CONFORMANCE.md`](AUTHORIZATION_CONFORMANCE.md). This section is the registration
and will hold the archived record as phases complete.

Compliance baseline (2026-10-15, before the 2026-10-18/19 work; the failure list and row counts below are that baseline, not current status, which is in section 11 of `docs/IDENTITY_PLATFORM_PLAN.md`): the framework covers ~60–70% of the BRS P1 scope — the authorization
engine (permissions, scopes, deny precedence, delegations, SoD, approval authority), multi-tenancy with
database isolation, the hash-chained security audit and the OpenIddict identity server (MFA, passkeys,
step-up, SSO/SCIM/LDAP/SAML, sessions). Source-verified truth-table failures: AUTH-08/09 (no
`MISSING_ATTRIBUTE` reason or finding sink), AUTH-12 (no creation/resulting-scope validation),
AUTH-23/24 (job envelopes carry no principal; no execution-time re-authorization), AUTH-25 (no
agent/delegated-principal context), AUTH-30 (no catalog lifecycle or change notice), AUTH-31 (no
share primitives), AUTH-21/22 partial (`IFieldAuthorizer` exists but is AI-connector-only
wiring). Verified structural findings: canonical identity + per-tenant memberships already exist
(ADR-01 is an additive extension, not a migration), and there is no cross-request decision cache by
design (next-request freshness — propagation is measured, not built). Row statuses flip only when their
conformance fixtures run (registry seeded: 8 fail · 2 partial · 24 unproven; AUTH-03/33 also await
phase assignment).

Work is split in three layers:

- **Layer A — framework (engine) extensions**, generic IAM primitives only, each on the
  completion-evidence template (current state → gap → change → conformance rows → tests → migration →
  TFMs → exit criterion):
  A1 principal kinds + person key, additive on the existing identity/membership model (P1a) ·
  A2 formal lifecycle + transition history beside the four existing state concerns — user
  `IsActive`, lockout, membership `IsActive`, company `TenantStatus` (P1a) · A3 creation and
  resulting-scope guard extending `ScopeEnforcer` + the cross-tenant write-guard pattern, DS-15
  (P1a) · A4 `MISSING_ATTRIBUTE` denial + data-quality findings sink, AS-05 (P1a) · A5 generic
  attribute dimensions (typed keys + effective dating exist; adds value hierarchies beyond org
  units), DS-03 (P1a core/P1b full) · A6 job initiating principal + execution-time re-authorization
  + `SystemJob` identities, AZ-18..21 (P1b) · A7 field-security wiring of the existing
  `IFieldAuthorizer` into read/write paths + output safety net, DS-10 (P1b) · A8 record sharing +
  masked references, gated behind core scope gates, DS-05/09 (P1b) · A9 agent authorization
  (rights-intersection), AZ-17/22 (P1b) · A10 permission catalog lifecycle + rename migration +
  change notice extending `PermissionDefinition`/`Freeze()`, RP-13/14 (P1a) · A11 audit retention
  engine extending the existing chain/anchors — never rebuilding them (P1c engine) · A12
  authentication hardening (absolute session lifetime, progressive lockout, breached-password
  check, admin session endpoints = P1c; assurance matrix, network/time rules, impersonation,
  risk signals = P2) · A13 SoD exceptions with compensating control (P2) · A14 security alert
  events (P2) · A15 governance completions (last-used, campaign auto-revoke, cumulative authority
  limits, role impact + four-eyes, existence-hiding 404/403) (P2).
- **Layer B — ERP-agnostic optional modules, in this repository** (the
  `Modulus.Authorization.Management` package pattern; UI stays in `modulus-ui`), built on framework
  seams only:
  B1 `AccessLifecycle` (JML orchestration, tracked leaver process, dormancy, rehire) · B2
  `AccessGovernance` (request catalog, campaign scheduling, SoD exception admin, findings) · B3
  `Notification` (channels, templates, EN/BN localization, preferences, tracking) · B4
  `ComplianceReporting` (RE-01..12, export governance) · B5 `AuditAdministration` (retention/holds/
  disposal admin, DSAR intake) · B6 `OrganizationProjection` (effective-dated sync from the ERP
  Organization module, calendars) · B7 admin + self-service UI in `modulus-ui`.
- **Layer C — acceptance**: the BRS §16.3 truth table becomes an automated conformance suite
  (`Modulus.Testing.Authorization` — one fixture per AUTH-01..34, deterministic shared test world,
  change-controlled per AS-09; spec in `docs/AUTHORIZATION_CONFORMANCE.md`); per-app
  `SecurityProbeSuite` + `TenantIsolationAssertions`; NFR benches for NF-03/06/09/19.

**Approved P1a order** (harness before identity-schema work): 1. conformance skeleton + traceability
map → 2. A3 → 3. A4 → 4. A10 → 5. A1/A2 (additive, migration-gated) → 6. B1 minimum. P1a is not
expanded — A6–A8, A11 retention, advanced A12 and A13–A15 stay in later phases.

**Mandatory repository gates** (definition of done for every affected work item): dual targeting
`net8.0;net10.0` with per-TFM versions; new public contracts in `PublicAPI.Unshipped.txt`;
InMemoryStoreCheck naming/defaults review for new stores; SecurityGuard + loosening-allowlist +
CLI template-test compatibility for any enforcement/default change; Central Package Management +
open-source-only dependencies; this registration pointer updated in the same change. Unresolved
policy inputs (retention durations, MFA assurance, NFR numbers, …) ship as documented provisional
defaults with a `Provisional` marker and a startup warning outside Development — never silently
treated as approved compliance policy.

Phasing mirrors BRS §19: P1a (A1–A5 core, A10 + B1 basics; exit: AUTH-01..02/04..14/26/30/34 green),
P1b (A5 full, A6–A9 + B6 + B3 minimum; exit: AUTH-21..25/27..29 green, no cross-channel bypass, NF
measured), P1c (A11 engine, A12 slice + B1/B4 full, IN-10; exit: all modules MC-01..06), P2
(governance layer; exit: AUTH-15..20/31..32 green), P3 (intelligence). Layer A schema additions are
listed in the plan's upgrade section — existing deployments need a migration; all changes are
additive.

<!-- source: approved plan "OpenBao tenant databases, setting management, navigation management" (phase 0, 2 and 3) -->
## Tenant secrets, settings and navigation: plan and status

Status (2026-10-23): **Phases 0 to 6 are built and tested.** Phase 3 has one open deployment decision (the existing-deployment migration, which the
upgrade script now covers); Phase 4 is complete in code, its `add-openbao` CLI included. Phase 5 (settings) and Phase 6 (navigation) are built as described below.
Phase 7 is the `modulus-ui` adapter, in that repository.

### Decisions

- OpenBao is called through the framework's own `HttpClient` (no vendor SDK), with the standard resilience handler on reads.
- The navigation core will live in Modulus, and `modulus-ui` becomes a renderer of it (Phase 6, 7).
- The first release covers KV v2 static secrets, the tenant catalog, the shared resolver, provisioning and rotation. Dynamic
  database credentials come later.
- A tenant connection string never sits in a database row or on `TenantInfo`. The catalog holds a pointer (`SecretPath`), and the
  connection string is the `connectionString` field of that secret.

### Phase 0: migration fan-out

- [x] Host pass of `MigrateModulusDatabasesForTenantsAsync` runs in host context (`Change(null)`).
- [x] Per-tenant failures are collected; the others still migrate; an `AggregateException` is thrown at the end
  (`TenantMigrationFanOutTests`).
- [x] Each tenant's targets are warmed before its migration (new): per-tenant contexts open synchronously during
  migration, so the connection interceptor needs the target resolved first.
- [x] This section and its Contents bullet (checked 2026-10-23).
- [x] Stale `/_ui/menu` reference: none remains in the "Modulus Improvement Plan" (checked 2026-10-23). The two remaining mentions (phase 2 and phase 5 notes) are historical and correct. The `samples/Meetup` call uses the single-database `MigrateModulusDatabasesAsync`, not the tenant fan-out, so nothing there is stale.
- [x] Verified the fan-out tests fail without the fix (2026-10-23): with `TenantDatabaseMigrationExtensions.cs` replaced by its pre-fix version (`e625a0e^`), `TenantMigrationFanOutTests` fails both rows (`BrokenTenant_IsReported_AndHealthyTenantStillMigrates`, `HostPass_RunsInHostContext_EvenWhenPerTenantContextIsRegistered`). With the fix, 4/4 pass with `TenantDatabaseMigrationTests` (net10.0).

### Phase 1: secret abstractions

- [x] `SecretReference`, `SecretValue` (redacted `ToString`), `ISecretStore`, `ISecretWriter` (check-and-set) in `Modulus.Core`.
- [x] `SecretStoreUnavailableException` (503), `SecretNotFoundException`, `TenantDatabaseUnavailableException` (503).
- [x] `CachedSecretStore`: fresh within `Ttl`, stale-if-error up to `MaxStaleOnFailure`, single-flight, misses not cached.
- [x] `ConfigurationSecretStore` (development), `InMemorySecretStore` (tests). `SecretsTests`: 25 tests.

### Phase 2: `Modulus.Secrets.OpenBao`

- [x] Options (`Secrets:OpenBao`), HTTP client with KV v2 read and check-and-set write, `X-Vault-Namespace`, CA certificate pinning.
- [x] Token lifecycle: Kubernetes, AppRole and token-file logins; renewal at two thirds of the lease; one re-login on 403.
- [x] Path prefix refused before any request; `http://` refused outside `AllowInsecureHttp`.
- [x] Read and write identities are separate. The writer is refused while `Secrets:RuntimeReadOnly` is true.
- [x] Health check against `sys/health`.
- [x] Assets in `deploy/openbao/`: `runtime-read.hcl`, `provisioner.hcl`, `bootstrap-dev.sh`.
- [x] Unit tests (`Modulus.Secrets.OpenBao.Tests`, 39) and container tests against `openbao/openbao:2.1.0`
  (`Modulus.Secrets.OpenBao.Integration.Tests`, 6), on net8.0 and net10.0.
- **Fixed during Phase 2:** the AppRole login went to `auth/kubernetes/login` because the mount defaulted to `kubernetes`. The
  mount now follows the method unless `MountPath` is set.

### Phase 3: catalog, resolver and per-tenant connections

Built:

- [x] `ITenantDataStoreCatalog` / `TenantDataStore` (`Modulus.Core`): isolation, provider key, secret path, expected secret
  version, status (`Provisioning`, `Active`, `Rotating`, `Disabled`).
- [x] `TenantDataStoreEntity` in table `ModulusTenantDataStores` (primary key `(TenantId, StoreKey)`; statuses and isolation stored
  as text), mapped in `TenantStoreDbContext`. `EfTenantDataStoreCatalog` is registered by `AddEfCoreTenantStore`.
- [x] `ITenantDatabaseResolver` and `TenantDatabaseTarget` (`ToString` never prints the connection string). The resolver
  (`Modulus.Secrets.TenantDatabaseResolver`, registered with `AddModulusTenantDatabases(configuration)`):
  - refuses a missing row, a status that does not serve, a provider mismatch, and a secret path that is not the canonical
    `{SecretPrefix}/{tenantId}/{storeKey}` (so a catalog edit cannot point at another tenant's secret);
  - re-reads a secret that is older than the catalog expects;
  - caches targets for `TenantDataStores:CatalogCacheTtl` (30 s);
  - returns a `null` connection string for a shared store, which means the host connection.
- [x] `TenantConnectionInterceptor` (`Modulus.EntityFrameworkCore.Isolation`): sets the connection string before the connection
  opens. The async open resolves; the synchronous open uses only a warm cache. In the host context nothing changes, and with no
  tenant the open is refused.
- [x] `AddModuleDatabasePerTenant<TContext>(storeKey, providerKey, hostConnectionString, configure)`. It registers a
  `TenantDatabaseRoute` that `AddIsolationInterceptors` turns into the interceptor, and it declares `DatabasePerTenant` isolation.
- [x] `TenantInfo.ConnectionString` is marked obsolete. No code in the repository set it.

Tests:

- `TenantDatabaseResolverTests` (Platform, 13 cases): active, rotating, missing row, non-serving statuses, provider mismatch,
  tampered path, shared store, missing secret or field, stale secret with eviction, secret-store outage, TTL caching,
  `TryGetCached`, invalid store keys, redaction.
- `TenantDatabaseInterceptorTests` (MultiTenancy, SQLite, real EF catalog and resolver): each tenant's rows land only in that
  tenant's database; a tenant without a row writes nothing; a synchronous open is refused until the target is resolved, then
  works.

Phase 3, remaining items (done in the second pass):

- [x] **Request path warming.** `ITenantWarmup` (Core) runs in `TenantMiddleware` right after the tenant is set. The EF
  `TenantDatabaseWarmup` resolves every per-tenant context's target, so synchronous opens in the request find it cached. A
  tenant whose database cannot be resolved gets `503` (`SERVICE_UNAVAILABLE`) before the application runs. The warmup is
  registered once by `AddModuleDatabasePerTenant`. Tests: `TenantMiddlewareTests` (503 before the application; warmups run for
  the resolved tenant), `TenantDatabaseInterceptorTests` (warmup resolves, refuses an unprovisioned tenant).
- [x] **Startup guard for the secret backend.** Deviation: the plan put this in `DataIsolationCheck`, but the check needs the
  Platform secret types, which the EF package does not reference. `TenantSecretBackendCheck` (Platform, registered by
  `AddModulusTenantDatabases`) refuses to start outside Development when the secret backend is `InMemorySecretStore` or
  `ConfigurationSecretStore`. Tests: `TenantDatabaseStartupTests`.
- [x] **Dapper / ADO.NET access.** `OpenTenantConnectionAsync` opens through EF, so the lease reaches the tenant database.
  Test: `DapperStyleLease_ReachesTheTenantDatabase`.
- [x] **Provider helpers.** `AddPostgreSqlPerTenantDatabase` (Modulus.Data.PostgreSQL) and `AddSqlServerPerTenantDatabase`
  (Modulus.Data.SqlServer), thin wrappers over `AddModuleDatabasePerTenant`.
- [x] **Existing deployments need the `ModulusTenantDataStores` table.** The framework still ships no migration for
  `TenantStoreDbContext` (a migration would fail on a database that already has the table). Instead, the idempotent upgrade
  scripts `docs/upgrades/identity-platform-2026-10.tenancy.{postgresql,sqlserver,mysql}.sql` create it. Each was run twice on
  PostgreSQL 17, SQL Server 2022 and MySQL 8.4. Applications that author their own migrations for `TenantStoreDbContext` must
  keep them in step with these tables.

### Extensible entities, step 1: tenant store (for review)

Applied the `IdentityDbContext<TUser, TRole, ...>` pattern to the tenant store. Steps 2 (organization) and 3 (identity user and role)
are not started.

- `TenantStoreDbContext<TTenant, TMembership, TDataStore>` is generic over its entity classes. The non-generic `TenantStoreDbContext`
  is the default, with the framework's entities.
- The stores (`EfTenantStore`, `EfTenantMembershipStore`, `EfTenantDataStoreCatalog`), `TenantManager` and
  `TenantDataStoreProvisioner` are generic over the same three types. Each one is reached through a non-generic interface
  (`IEfTenantStore`, `IEfTenantMembershipStore`, `ITenantDataStoreCatalog`, `ITenantDataStoreProvisioner`), so the scope bridges keep
  their shape. `TenantManager` keeps its non-generic name as the default subclass.
- Registration: `AddEfCoreTenantStore<TContext, TTenant, TMembership, TDataStore>(configure)`. The existing non-generic call is
  unchanged. `MigrateTenantStoreAsync<TContext>` migrates an application's context.
- Application pattern (tested by `TenantStoreExtensibilityTests`): derive the entity from `TenantEntity`, derive the context from
  `TenantStoreDbContext<...>`, register it through the generic overload. The extra column persists in the same table, and the
  framework's stores and manager work on the application's type.
- CLI: `modulus migrate add` scaffolds the table automatically for new apps (verified with `dotnet ef migrations add` against the
  generated factory shape). Existing apps get it from the next `migrate add`.

### Extensible entities, step 2: organization (authorization store)

The organization entities follow the same pattern as the tenant store. Step 3 (identity user and role) is not started.

- The authorization context is split in two. `AuthorizationStoreDbContextBase` (abstract) holds every non-organization set, its mapping
  and its tenant filters. `AuthorizationStoreDbContext<TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>` adds the four
  organization sets, their mapping and their tenant filters. The non-generic `AuthorizationStoreDbContext` is the default with the
  framework's own rows.
- The organization row classes (`OrgUnitRow`, `OrgUnitParentRow`, `OrgUnitProfileRow`, `CompanyProfileRow`) are public and unsealed,
  so an application can derive its own.
- The 15 non-organization stores depend on `IDbContextFactory<AuthorizationStoreDbContextBase>`, so they are unchanged in behaviour.
  The organization stores (`EfOrgHierarchy<...>`, `EfOrganizationProfileStore<...>`) are generic over the four types.
- Registration: `AddEfCoreAuthorizationStores<TContext, TOrgUnit, TOrgUnitParent, TOrgUnitProfile, TCompanyProfile>(configure)`. The
  existing non-generic call is unchanged. `ContextFactoryAdapter` presents an application's factory under the base type and the
  generic instantiation, since `IDbContextFactory<T>` is invariant.
- `IOrgUnitAdministration` (new) carries the add and re-parent operations that the management API uses, so the management extension no
  longer depends on the concrete default hierarchy class.
- Tests: `OrganizationExtensibilityTests` (an application org unit column persists; the hierarchy works on the application's type).
  Existing authorization EF tests (51) and management tests (85) pass on net8.0 and net10.0.

### Phase 4: provisioning and rotation (built, 2026-10-19)

Built:

- [x] `ITenantDataStoreProvisioner` (Core). `TenantDataStoreProvisioner` (`Modulus.MultiTenancy.EntityFrameworkCore`, registered by
  `AddEfCoreTenantStore`) writes the connection string with check-and-set and keeps the catalog row in step. It needs an
  `ISecretWriter` (the provisioning identity, for example `AddOpenBaoSecretWriter`) and an `ITenantDatabaseResolver`.
  - `ProvisionAsync` creates the row as `Provisioning`, writes the secret, then marks it `Active`. It is idempotent for an active
    store. A store left `Provisioning` by an interrupted run is completed (the write is against the secret's current version).
    A disabled store and a provider mismatch are refused.
  - `RotateAsync` moves the store to `Rotating`, writes the new string with check-and-set against the current version, and marks it
    `Active` again. A failed rotation stays `Rotating` (still served) and can be retried. An unprovisioned tenant gives not found.
  - Both evict the resolver's cached target, so the next request on this node reads the new value.
- [x] `InMemorySecretStore` now follows OpenBao's check-and-set rule. It had treated `expectedVersion: 0` as "must be 0", which
  never matches an absent secret, so a fresh provision would have failed. Regression test: `InMemorySecretStoreCasTests`.
- Tests: `TenantDataStoreProvisionerTests` (provision, idempotent provision, resuming an interrupted provision, refusal of a disabled
  store, rotation with the resolver seeing the new string, rotation of an unprovisioned or non-active store).

Completed in the third pass:

- [x] **Probe and setup migration.** Provisioning opens the candidate string through `ITenantDatabaseProviders.ProbeAsync` before
  it writes anything; a refused probe changes nothing and leaves the store `Provisioning`. The write is followed by the tenant's
  migration (`MigrateOrCreate`, the framework default) and only then `Active`. The store is still `Provisioning` during the
  migration, so the resolver refuses it; the migration reaches its database through `TenantDatabaseSetup.Use`, an override that
  the interceptor honours for one tenant, one store and one provider. Test: `Provisioning_a_new_tenant_creates_its_schema_...`.
  The probe is `SELECT 1` through the provider's connection factory, registered with `AddTenantDatabaseProvider` (PostgreSQL and
  SQL Server helpers register theirs; a store for a provider with none fails with a clear message).
- [x] **Cross-node invalidation.** `TenantDataStoreChanged` (`tenancy.tenant-data-store-changed.v1`, marker `TenancyArea`) is published
  through `IModuleBus` after a provision or rotation. `TenantDataStoreChangedHandler` evicts the node's cached target and clears the
  pool of the string it replaced: the resolver keeps each target it evicts (`TryTakeSuperseded`), and the handler clears that
  string's pool through `ITenantDatabaseProviders.ClearPool`. **Operational requirements:** the event reaches other nodes only through
  a broker- or outbox-backed bus, and a broker or outbox consumer can route it only when the Platform assembly is passed to
  `AddModulusEvents` (the registry is filled from those assemblies). Without that, other nodes see the change when their cache entry
  expires (`CatalogCacheTtl`, 30 s by default). A failed publish fails the call after the change is stored; the change is not undone.
  Tests: `Rotate_ClearsThePoolOfTheReplacedString_WhenTheChangeIsHandled`, `Provision_PublishesTheChange_OnceTheStoreIsActive`.
- [x] **Admin API.** `MapModulusTenantProvisioning("/api/tenancy/data-stores")` (`TenantDataStoreEndpoints`): `GET {tenantId}/{storeKey}`,
  `POST {tenantId}/{storeKey}` (provision) and `POST .../rotate`. Every route needs `tenancy:data-stores:manage` (Critical, so step-up
  applies when it is on; registered by `AddModulusTenantProvisioningAdmin`). The routes run in host context, validate the store key
  and the required fields, and never return a connection string. Map it only on the provisioning host. Tests:
  `TenantDataStoreEndpointTests` (in-process server; the policy is open there, so the permission itself is not exercised).
- [x] **Metrics and audit.** Counters `modulus.tenant_data_store.provisioned` / `.rotated` and a security audit event per change, naming the
  store and version only.
- [x] **`modulus add-openbao [--auth kubernetes|approle] [--tenant-databases]`.** Writes `deploy/openbao/` (the runtime and provisioning
  policies and the dev bootstrap), `docker-compose.openbao.yml`, the `Synvro.Modulus.Secrets.OpenBao` package reference, the store in
  `Configuration/FeatureServicesSetup.cs` (and `AddModulusTenantDatabases` with `--tenant-databases`), and the `Secrets` settings for
  production (placeholder address, read-only) and Development (dev server over http, AppRole files under `secrets/`). Idempotent.
  The dev AppRole files must be git-ignored; the command prints that. The template names avoid `.sh` and dashes, because MSBuild reads a
  `.sh` extension as a culture and drops the resource from the main assembly. Tests: `OpenBaoCommandTests` (10).
- [x] **Doctor check.** When the API host wires OpenBao, `modulus doctor` probes `{Address}/v1/sys/health` (3 s); any HTTP answer passes
  (OpenBao reports its state in the code), no answer is a warning.

Still open in Phase 4 (deployment, not code):

- [x] **Provisioning host (built 2026-10-24).** `modulus add-openbao --provisioner` (PostgreSQL and SQL Server) generates `src/Provisioning/{App}.Provisioning`: a separate web host, the only one with an OpenBao write identity. It registers `AddOpenBaoSecretWriter` (refused unless `Secrets:RuntimeReadOnly` is false), the EF tenant catalog and its probe, and serves only `MapModulusTenantProvisioning` behind `tenancy:data-stores:manage` (Critical). The grant store defaults to an empty one, so nobody can provision until it points at the API host's authorization database (fail closed). Verified on a generated PostgreSQL app against the local pack: it builds with 0 warnings; it starts in Development; `/health/live` answers 200 through a reviewed `Loosen`; the provisioning routes answer 401 to anonymous callers; and with `Secrets:RuntimeReadOnly=true` it refuses to start. Tests: `ProvisioningHostTests` (15 rows); the CLI suite is 602/602 on net10.0. **Authenticated path (verified 2026-10-24, against a generated API on PostgreSQL):** the API's token server issued an admin access token; the provisioning host validated it (signature, issuer, lifetime) and refused the provisioning route with 403, because the host's grant store holds no `tenancy:data-stores:manage` grant. That is the authenticated-but-unauthorized path working. Two findings from that run: the API must issue unencrypted access tokens (`Identity:EncryptAccessTokens=false`), since the provisioning host cannot read encrypted ones; and the identity module could not issue an audience claim; it now can, through `Identity:Audiences` (every access token carries the configured audiences; test `AccessTokenAudienceTests`, identity suite 426/426 on net10.0 and net8.0). The host checks the audience only when `Provisioning:Audience` is set, and the generator's hint tells the operator to set both to the same value (the generated Development settings leave it empty). **Success path (verified 2026-10-23, generated PostgreSQL API and provisioning host, OpenBao dev server).** The API keeps its grants in the Authorization database (`AddEfCoreAuthorizationStores`) and writes its first-boot seed once (`SeedPermissionGrantsAsync`, guarded by a marker row in `ModulusAuthorizationSeeds`); the provisioning host reads the same database. An administrator with `tenancy:data-stores:manage` provisioned a tenant: `POST` returned 200 (`Active`), the connection string is in OpenBao at `modulus-dev/tenant-db/{tenant}/primary`, a repeated `POST` is idempotent, `GET` returns the row, an anonymous call gets 401, and the same call without the grant gets 403. Defects found and fixed on the way: (1) the host used `AddMultiTenancy()`, which fails closed on an unresolved request, so it could not see the host-level grants; it now runs in the host context (`NullCurrentTenant`, a singleton). (2) Two hosts on one database each reconciled the whole permission catalog, so one host's startup deleted the rows the other declared; the provisioning host no longer writes the catalog, and the API is its only writer. (3) Revoking the last seeded grant re-seeded it on the next boot; a seed now leaves a marker. Verified also: the generated PostgreSQL app and a fresh SQLite app each pass their own tests (9/9), the CLI suite passes (608/608), and the Authorization EF suite passes (108/108, including five seed tests). **Known limits:** the shared catalog keeps one set of rows per host (`PermissionCatalogOptions.Owner`), so a host's restart never touches another host's permissions; this replaced an earlier single-writer workaround that let one host's startup delete another's rows. An existing `ModulusPermissionCatalog` table needs the upgrade in `docs/upgrades/identity-platform-2026-10.authorization.postgresql.sql` (new `Owner` column, primary key `(Owner, Permission)`; existing rows take the API host's owner). The authorization store, the seed marker and the tenant catalog ship no migrations, so a production deployment must author them; without them the first grant read or write fails. An existing deployment needs the new `ModulusAuthorizationSeeds` table. The generator now covers SQLite and MySQL as well (2026-10-25). An end-to-end run on each engine (generated API with the OpenBao dev server, a provisioning call with an audience-bearing token, a grant check and an OpenBao secret check) returned 200 for a granted admin, 403 without the grant and 401 anonymously. Running it found and fixed: (1) the authorization store and the identity store could not be created on MySQL: the scoped-grant unique key and OpenIddict's token index exceeded MySQL's 3072-byte index limit (the scoped `Scope` is 200 characters on MySQL; OpenIddict's token columns get explicit lengths there); (2) the grant, position, approval and dimension queries sent a captured list as one collection parameter, which the MySQL provider cannot type (they now use scalar comparisons, with the same results on SQLite: authorization suite 116/116); (3) the generated SQLite provisioning host lacked the patched native SQLite library (its build reported a known advisory); (4) the generated code passed a nullable connection string to the MySQL provider's `UseMySQL`, which warned under `TreatWarningsAsErrors`. The index limit is guarded by unit tests (`ScopedGrantKeyTests`, `MySqlIndexLimitTests`), and the query translations by `MySqlQueryTranslationTests`. |
- [x] **Production runbook, Modulus side (written 2026-10-23).** The `add-openbao` output is a development setup. For production:
  1. **Schema.** The authorization store, its seed marker and the tenant catalog ship no migrations. Author them in the application (a context derived from `AuthorizationStoreDbContext` / `TenantStoreDbContext`, with `dotnet ef migrations add`) and apply them before the host starts. Outside Development the helpers refuse to start without migrations (`MigrateAuthorizationStoreAsync` / `MigrateTenantStoreAsync` throw "No migrations are compiled").
  2. **Write identity.** Only the provisioning host runs with `Secrets:RuntimeReadOnly=false`; the API host stays read-only, and `AddOpenBaoSecretWriter` refuses to register anywhere else.
  3. **Grants.** The first-boot seed is written once per database. Grant `tenancy:data-stores:manage` (Critical) to the administrators through the grant API, which needs `authorization:manage`; never by editing the seed.
  4. **Catalog.** The shared permission catalog keeps one set of rows per host (`PermissionCatalogOptions.Owner`, the assembly name by default). Set the owner explicitly when the assembly name may change, because a new owner orphans the rows recorded under the old one.
  5. **Tokens and audience.** The API issues unencrypted access tokens (`Identity:EncryptAccessTokens=false`) so the provisioning host can validate them. Set `Provisioning:Audience` once the token server issues an audience; until then the permission check is the only gate.
  6. **Login identities.** Deploy the policies under `deploy/openbao` (runtime read for the API, provisioner for the writer) and bind them to the Kubernetes service accounts or AppRole ids the platform issues. Deliver secret ids as files, and rotate them.
  7. **Existing deployments.** Add the `ModulusAuthorizationSeeds` table (seed marker) and the `ModulusTenantDataStores` table (Phase 3) with migrations before the first start.
- [ ] **Operator-owned (not code):** Raft cluster sizing, TLS certificates, auto-unseal, audit devices, snapshots and the Kubernetes role bindings. These depend on the platform and are not verified here.
- [x] The existing-deployment migration for `ModulusTenantDataStores` (Phase 3, above): `docs/upgrades/identity-platform-2026-10.tenancy.{postgresql,sqlserver,mysql}.sql` (2026-10-23; run twice per engine, not yet compared with EF's DDL).

### Phase 5: setting engine (built, 2026-10-23)

Decisions (taken by the engineering team; the owner may revise them):

- **Typed definitions.** `SettingDefinition` gains `ValueType` (`SettingValueType`: string, boolean, int32, int64, decimal, guid,
  time span), `AllowedValues` (case-insensitive, for choice settings) and `AllowedScopes` (`SettingScopes` flags). A write is checked
  against the definition: a value that does not parse, is not allowed, or is at a scope not listed is refused with 400. Values are
  stored as text, at most 4000 characters, for every name (registered or not).
- **Pins.** `Settings:Pinned:{name}` (configuration) fixes a value for every caller and every scope. A pin wins over the user, tenant
  and global values, and a write to a pinned name is refused with 409. Pins come from configuration, so a pin is a deployment decision.
- **Resolution.** `ISettingManager.ResolveAsync` returns the effective value and its source (`Pinned`, `User`, `Tenant`, `Global`,
  `Default`, `None`). `GetOrNullAsync` is unchanged for callers.
- **Scope safety.** A company-scoped write with no company in scope, or a person-scoped write with no signed-in person, is refused
  (400). Before this fix such a write silently changed the installation-wide value.
- **Administrator's view.** `ISettingAdministration`: `ListAsync` (every registered definition with its effective value, source and
  pin state), `SetAsync` and `RemoveAsync`. Only registered names can be written, so an administrator cannot create an arbitrary setting
  (404 otherwise).
- **HTTP API.** `MapModulusSettingAdministration()` (default `/api/settings`): `GET`, `PUT|DELETE tenant/{name}` and `user/{name}` need
  `settings:manage` (Sensitive); `PUT|DELETE global/{name}` also needs `settings:global:manage` (Critical), so a company administrator
  cannot change every company's value.
- **Storage.** `EfSettingStore` (`AddEfCoreSettingStore()`, package `Modulus.MultiTenancy.EntityFrameworkCore`) keeps values in
  `ModulusSettingValues` in the tenant store database. The key is `(Name, ScopeKey)`, where `ScopeKey` is one non-null text column
  (`global`, `tenant:{id}`, `user:{tenantId or none}:{userId}`), so the key is unique on every provider. The table has no migration in
  the framework; the upgrade scripts add it for existing databases, and a new application's tenant store migration includes it.
- **Cache and version key.** `CachedSettingStore` caches each read for `Settings:CacheDuration` (30 seconds by default, zero turns it
  off), misses included. Every cached value is keyed by a process-wide `SettingCacheVersion`, and a write advances it, so a write on
  a node makes that node's cache stale at once. **Other nodes (built 2026-10-23):** `ISettingChangeNotifier` is called after each
  committed write and removal. The default does nothing (one node). `AddRedisSettingCacheBackplane()` (`Modulus.Caching.Redis`, on the
  same Redis connection as `AddRedisCacheService`) publishes the change on `modulus:settings:changed`, and every other node advances its
  own version. A node ignores its own message (the origin is per notifier instance). Delivery is best effort: a lost message leaves the
  other nodes stale until their cache entries expire, so `Settings:CacheDuration` remains the bound on staleness. Tests:
  `RedisSettingChangeBackplaneTests` (2, Testcontainers Redis, both targets) and `SettingsPhase5Tests` (notifier calls).
- **Concurrency.** Two writes to the same setting at the same scope at once can both insert; one fails on the key and the caller sees
  the exception. Settings change rarely; a retry repeats the write.

Tests: `SettingsPhase5Tests` (typed, allowed-value and scope checks, pins, source, administrator view and refusals, cache, binding),
`SettingStoreTests` (EF store isolation and upsert, the cache seen through the manager, the HTTP routes and permissions).

### Phase 6: navigation engine (built, 2026-10-23)

- **Items and contributors.** `NavigationItem` (key, title, parent, URL, permission, icon, order). A module declares its items with
  `INavigationContributor` (`AddNavigationContributor<T>()`); a host can add inline items to `AddModulusNavigation(configure)`. The menu
  is built once, on first use, from every contributor in registration order.
- **Checked when the host starts.** `NavigationStartupCheck` (after every `StartAsync`) refuses a menu with a duplicate or invalid
  key, a missing or looping parent, a depth over 8 levels, a blank title, a URL that leaves the application (scheme, `//` or a
  backslash), or a permission that is not registered.
- **Per person.** `GET /api/navigation/menu` (any signed-in caller) returns the tree: an item shows when the caller holds its
  permission (the same `PermissionRequirement` as the endpoints) and no override hides it. A group shows only while a child does.
  An item with no permission shows to every signed-in caller.
- **Company overrides.** `ModulusNavigationOverrides` is not a separate table: an override is a setting (`navigation.override.{key}`,
  company scope, JSON). It can hide an item and everything under it, retitle it, reorder it, or move it under another item. It cannot
  reveal an item the permission hides, and it cannot change the link or the permission, so an override only narrows the menu. Writes
  are checked: the item and new parent must be declared, the new parent may not loop back, the depth stays within the limit, and the
  title must be non-blank. A write with no company in scope is refused (400).
- **Admin API.** `MapModulusNavigation()` (default `/api/navigation`): `GET menu` (signed in), `GET definitions` (every item with its
  override, hidden ones included), `PUT|DELETE overrides/{key}` (both need `navigation:manage`, Sensitive). Administration routes are a
  separate route group, so the menu route does not inherit their permission.
- **Generated applications.** An OpenIddict API maps the menu (`AddModulusNavigation()` and `MapModulusNavigation()`, from
  `FeatureServicesSetup` and `FeatureEndpoints`). The generated application declares no items yet, so its menu is empty until a module
  adds a contributor. Overrides use the in-memory setting store until `AddEfCoreSettingStore()` is added with the tenant store, so a
  generated application loses overrides on restart; `navigation:manage` is granted to no role by default (fail closed).
- **Not built.** Per-person item visibility beyond permissions (for example feature flags or per-company entitlements in the menu),
  per-user overrides, and the `modulus-ui` renderer (Phase 7).

Tests: `NavigationTests` (visibility and order, groups, overrides, write checks, definition checks, the startup permission check),
`NavigationEndpointTests` (the menu's sign-in requirement, the administration permission, override and removal through HTTP).
`NavigationWiringTests` (CLI) covers the generated wiring.

### Phase 7: `modulus-ui` adapter (separate repository, not started here)

### Verification so far

- Unit, on net8.0 and net10.0: `Modulus.Core.Tests`, `Modulus.EntityFrameworkCore.Tests`, `Modulus.MultiTenancy.Tests` (84),
  `Modulus.Platform.Tests` (328), `Modulus.Secrets.OpenBao.Tests` (39), `Modulus.AspNetCore.Tests`. All pass.
- Integration, on net8.0 and net10.0: `Modulus.Secrets.OpenBao.Integration.Tests` (6, Docker).
- `dotnet format --verify-no-changes` is clean for every project touched.
- Not run yet: the full `dotnet build modulus.slnx` and the full unit run.
