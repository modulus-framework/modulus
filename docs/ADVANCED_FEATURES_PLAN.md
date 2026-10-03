# Advanced features: hybrid cache, per-client BFFs, gRPC, webhooks, GraphQL, realtime, MCP

Roadmap for the advanced features Modulus lacked: a hybrid cache, a Backend for Frontend per client type,
gRPC, webhooks, GraphQL, realtime push and MCP.

Status: **Phase 1a (FusionCache) and phase 1b (`Modulus.Bff`, follow-ups included) are implemented and validated.**
The full solution builds with 0 warnings, every `Category=Unit` suite passes (`Modulus.Bff.Tests` 58,
`Modulus.Cli.Tests` 481) and `dotnet format --verify-no-changes` is clean. Phases 2 to 6 are not started.
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
| 6 | `Modulus.Mcp` (commands and queries as AI tools) | later |

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
| Edge rules | — | `X-App-Version`/`X-App-Platform` gate (`426`), weak ETag + `304`, rate limit per `X-Device-Id` | `Idempotency-Key` required on writes (`400`), rate limit |

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

### Phase 6: MCP (`Modulus.Mcp`)

- [ ] Commands and queries marked `[McpTool]` become MCP tools, called through `IMediator` with bearer tokens and
  permission checks.

## Verification checklist (every phase)

1. `dotnet build modulus.slnx`: 0 warnings, 0 errors (PublicAPI analyzers included).
2. `dotnet test modulus.slnx --filter "Category=Unit"`; integration tests with Docker where they exist.
3. `dotnet format modulus.slnx --verify-no-changes`; `dotnet list modulus.slnx package --vulnerable`; license of
   every new package checked against the policy table.
4. End to end on an app generated from freshly packed packages.
