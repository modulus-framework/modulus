# Advanced features: hybrid cache, per-client BFFs, gRPC, webhooks, GraphQL, realtime, AI platform integration

Roadmap for the advanced features Modulus lacked: a hybrid cache, a Backend for Frontend per client type,
gRPC, webhooks, GraphQL, realtime push and integration with a separately built AI platform.

Status: **Phase 1a (FusionCache) and phase 1b (`Modulus.Bff`, follow-ups included) are implemented and validated.**
The full solution builds with 0 warnings, every `Category=Unit` suite passes (`Modulus.Bff.Tests` 65,
`Modulus.Cli.Tests` 593) and `dotnet format --verify-no-changes` is clean. Phases 2 to 5 are done (see each
phase); phase 6 (AI platform integration) is built except `Modulus.UI.AI` (6a–6d done).
Checkboxes are updated as work lands; where the build differs from the plan, the original text is kept and
the difference is noted inline as **As built:**.

## Context

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

## Open-source package policy (every phase)

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

## Topologies: modular monolith and microservices

| Feature | Modular monolith | Microservices |
|---|---|---|
| FusionCache | L1 per replica; add the Redis L2 + backplane at more than one replica | Each service has its own `CacheName` (key prefix), so shared Redis never collides; cross-service invalidation goes through integration events, never shared keys |
| BFF upstreams | One upstream (`api`) | Many upstreams (`catalog`, `orders`, ...), one YARP cluster and typed client each |
| Addressing | `Bff:Services:{name}:Address` (`Api:BaseUrl` is the fallback for `api`) | Same, or service discovery (`Bff:UseServiceDiscovery`, `https+http://catalog`) |
| Token server | OpenIddict in the API host | Any supported auth server or an identity service, reached through `Bff:Authority` and OIDC discovery |
| Aggregation | Several module endpoints on one host | Fan-out across services; per-section timeouts and degraded responses |
| gRPC (phase 2) | Optional | The main service-to-service transport |

## Roadmap

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

## Phase 1a: FusionCache hybrid cache (done)

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

## Phase 1b: `Modulus.Bff`, one backend per client type (done)

### Model

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

### Clients (`BffClientKind`)

| | **Web** (SPA) | **Mobile** (native app) | **Partner** (machine to machine) |
|---|---|---|---|
| Inbound auth | Cookie session (`.bff.{name}`); sign-in by OIDC code + PKCE or the password grant | Bearer: JWT against the JWKS, or RFC 7662 introspection | Bearer from client credentials (JWT or introspection) |
| Tokens upstream | Server-side store, never in the browser | Caller's bearer forwarded after client-id and scope checks | Forwarded |
| CSRF | `X-CSRF: 1` required, else `401` | none | none |
| Session endpoints | `GET /bff/login` (OIDC) or `POST /bff/login` (password), `/bff/user`, `POST /bff/logout` | `/bff/me` | `/bff/me` |
| Edge rules | — | `X-App-Version`/`X-App-Platform` gate (`426`), weak ETag + `304`, rate limit per caller (user, else client id; never a client-chosen header) | `Idempotency-Key` required on writes (`400`), rate limit |

### Components (`src/platform/Modulus.Bff`)

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

### CLI

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

### Tests

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

### Verification (done)

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

## Later phases (each gets its own detailed plan when started)

### Phase 2: gRPC (`Modulus.Grpc`) (done)

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
  wires the API host (`Cobytelabs.Modulus.Grpc` reference, `AddModulusGrpc` + `MapModulusGrpc`, a `Grpc` settings
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

### Phase 3: Webhooks (`Modulus.Webhooks`) (done)

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

### Phase 4: GraphQL (`Modulus.GraphQL`) (done)

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

**Not built:** subscriptions, persisted queries, cursor pagination/filtering conventions, a FusionCache-backed
response cache (queries are already cached by `[CacheFor]` through the mediator).

### Phase 5: Realtime (`Modulus.Realtime`) (done)

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
**Gap:** generated CRUD declares `{Entity}CreatedIntegrationEvent` but does not publish it, as for webhooks.

### Phase 6: AI platform integration (`Modulus.AI.*`)

Replaces the earlier one-line "MCP" phase.

#### Context

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

#### Split of responsibility (the AI vision against the two products)

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

#### Packages

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

#### 6a: Connector foundation

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

#### 6b: Data for answers and the index

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
- **Not built.** There is no `Search` for extension fields (`ExtraProperties`), and no `ISearchContributor` for
  cross-entity search.

#### 6c: Host integration, UI and CLI

- [ ] **`Modulus.UI.AI` (deferred).** Not built: the UI is going to change, so the assistant host waits for it.
  The design stays:
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
    - adds `Cobytelabs.Modulus.AI.Connector` and `.EntityFrameworkCore` to the API host;
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

#### 6d: Conformance

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
- Not covered generically: timeout-as-deny (needs a slow capability in the app), and response shapes against the
  platform's published OpenAPI spec (not yet published).
- [ ] **Against the real platform.** Run the platform's `AiPlatform.Integrations.Conformance` suite against a
  generated app, in the platform's or the app's CI, before a connector is activated. The in-repo kit mirrors its
  categories but does not replace it.

#### 6e: Waiting on platform decisions (not designed here)

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

#### Remaining work (phase 6)

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
- [ ] Access-change hook for grant-store writes made outside the admin API.
- [ ] Durable revocation queue: move pending signals to the outbox so a shutdown cannot lose them.
- [ ] Shared envelope replay cache (distributed cache) for hosts with several replicas.
- [ ] Batch record lookup for `/extract` and `/changes`, which today cost N+1 queries per page.
- [ ] `Search` over extension fields (`ExtraProperties`) and an `ISearchContributor` for cross-entity search.
- [ ] Per-user masking of `[PersonalInformation]` fields (today: declared `Restricted`; add `[Classified]` to mask).
- [ ] Store-generated and composite keys for `[AiIndexed]` entities.
- [ ] At-least-once change hints (today at most once; `/changes` polling is the safety net).
- [ ] Aggregate query shapes run in CI on PostgreSQL, SQL Server and MySQL (only SQLite today).
- [ ] A timeout-as-deny check in the conformance kit (needs a deliberately slow capability).
- [ ] `generate-crud --ai` on an older CRUD set and the `ai_changes` migration stay manual steps (`add-ai` prints them).

#### Critical files

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

#### Tests

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

#### Verification

1. Generate an app: `modulus app --kind api --auth openiddict`, then `add-ai`, then
   `generate-crud Product --ai`. It must build with 0 warnings and the generated tests must pass (done).
2. The conformance kit (`AiConformanceTests`, fake platform) passes against it (done: 15/15, and 16/16 with
   `--multi-tenancy`). The platform's own suite is still to run.
3. (Deferred with `Modulus.UI.AI`.) A web app shows the assistant to a signed-in user with `ai:use`, and its session endpoint
   refuses anonymous callers.

## Verification checklist (every phase)

1. `dotnet build modulus.slnx`: 0 warnings, 0 errors (PublicAPI analyzers included).
2. `dotnet test modulus.slnx --filter "Category=Unit"`; integration tests with Docker where they exist.
3. `dotnet format modulus.slnx --verify-no-changes`; `dotnet list modulus.slnx package --vulnerable`; license of
   every new package checked against the policy table.
4. End to end on an app generated from freshly packed packages.
