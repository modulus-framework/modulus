# Modulus — Repository Structure

Modulus is an enterprise-grade **modular monolith framework for .NET 10**,
shipped as focused NuGet packages plus a `dotnet tool` CLI. This document maps
the repository so a newcomer can find the right project fast. For feature-level
documentation see [README.md](README.md); for the authorization design see
[docs/architecture/authorization-framework-blueprint.md](docs/architecture/authorization-framework-blueprint.md).

## Top level

```
modulus/
├── modulus.slnx                 # Solution (slnx format)
├── Directory.Build.props        # Thin root; imports build/*.props
├── Directory.Packages.props     # Central Package Management (see pin policy comment)
├── build/                       # Modulus.Common.props / Modulus.Packaging.props / Modulus.Test.props
├── src/                         # Framework packages (see below)
├── cli/                         # Modulus.Cli — `modulus` dotnet tool (Spectre.Console + Scriban)
├── tests/                       # unit/ (xUnit, Category=Unit) and integration/ (Testcontainers)
├── samples/                     # Storefront (`modulus app` output) + cobytemed-erp-app
│                                 # (real app retrofitted onto Modulus) — see their READMEs
├── docs/architecture/           # Design blueprints
├── IMPROVEMENT_PLAN.md          # Living decision log: completed P0/P1 fixes + backlog
└── .github/workflows/           # ci.yml (build/format/test/pack), codeql.yml, release.yml
```

## `src/` — framework packages

| Area | Project | Contents |
|---|---|---|
| core | `Modulus.Core` | Module system (`IModule`, `ModulusModule`, `[DependsOn]`, `ModuleLoader`, `ModulusBuilder`), DDD primitives (`AggregateRoot`, `ValueObject`), entity markers (`ISoftDelete`, `IHasTenantId`, `IHasOrgUnit`, `IHasOwner`, `IHasWorkflowState`, `[Classified]`), core seams (`ICurrentUser`, `ICurrentTenant`, `ICurrentDataScope`, `IFeatureGate`, `IPermissionRegistry`) with Null defaults, correlation context |
| core | `Modulus.AspNetCore` | `AddModulus`/`UseModulus`, REPR endpoints, global exception handler (ProblemDetails), correlation middleware, CORS, security headers, rate limiting, API versioning, OpenAPI transformers, idempotency, feature flags (FeatureManagement wrapper), secrets guard, personal-data protection |
| data | `Modulus.Data.Abstractions` | `IRepository`, `ISpecification`, `ISearchRepository` |
| data | `Modulus.EntityFrameworkCore` | `ModuleDbContext` (outbox enqueue, audit stamping, combined soft-delete∧tenant∧org-scope query filters, PII encryption), `EfRepository`, `IEntityContextMap`, migration helpers |
| data | `Modulus.Data.{SqlServer,PostgreSQL,MySQL,SQLite,MongoDB}` | Provider registration packages |
| identity | `Modulus.Identity` | OpenIddict server (auth-code + refresh; ROPC opt-in), ASP.NET Identity integration, `ClaimsPrincipalCurrentUser`, grant-store permission checker, 6 external IdP adapters (Auth0, Authentik, Azure AD, Duende, Keycloak, Okta) |
| messaging | `Modulus.Mediator` | `ICommand`/`IQuery` handlers, pipeline behaviors (logging, validation, feature gate, authorization, caching, transaction) via compiled delegates |
| messaging | `Modulus.Events` | Domain-event dispatcher, integration-event envelope + stable-name registry, in-process bus |
| messaging | `Modulus.Outbox(.Abstractions)`, `Modulus.Outbox.MongoDB` | Transactional outbox: same-transaction enqueue, atomic claim, backoff, dead-letter |
| messaging | `Modulus.Inbox`, `Modulus.Inbox.MongoDB` | Idempotent consume (claim-by-EventId decorator) |
| messaging | `Modulus.EventBus.{RabbitMQ,Kafka}` | `IModuleBus` broker transports |
| messaging | `Modulus.Sagas` | Rebus-based orchestration |
| platform | `Modulus.Platform` | Authorization stack (grants, org scope, data scope, resource/workflow policies, field security, feature entitlements, delegation/SoD/governance), multi-tenancy (resolvers, middleware, `CurrentTenant`), background job queue, memory caching, local file storage, SignalR base, resilient HttpClient |
| platform | `Modulus.MultiTenancy.EntityFrameworkCore` | EF tenant store (`TenantEntity`, `TenantManager`) |
| platform | `Modulus.Authorization.EntityFrameworkCore` | Durable EF-backed authorization stores (grants, org hierarchy/placements, feature entitlements, delegations) superseding the in-memory defaults |
| platform | `Modulus.Authorization.Management` | Admin REST API (`MapModulusAuthorizationManagement`) over the EF authorization stores, guarded by `authorization:manage` |
| platform | `Modulus.AspNetCore.Redis` | Redis-backed shared HTTP idempotency store (`AddRedisIdempotencyStore`) |
| platform | `Modulus.Caching.Redis`, `Modulus.Storage.{S3,AzureBlobs}`, `Modulus.SignalR.Backplane` | Dependency-heavy providers split from Platform |
| observability | `Modulus.Observability` | OpenTelemetry wiring, tracing behavior, module health + module-graph endpoints |
| testing | `Modulus.Testing` | `ModulusWebAppFactory<TEntryPoint>` (real host on throwaway SQLite), header-driven test auth |

## Conventions (enforced or expected)

- **0 warnings**: `TreatWarningsAsErrors` is global; XML docs are required on public APIs.
- **Modules**: inherit `ModulusModule`, declare deps with `[DependsOn(typeof(...))]`,
  register via `builder.Services.AddModulus<AppHostModule>(builder.Configuration)`.
- **Cross-cutting features** follow the shape documented in `ROADMAP_TIER3.md` →
  "Shared conventions": config-bound `XxxOptions`, `AddModulusXxx`/`UseModulusXxx`,
  swappable services registered with `TryAdd`.
- **Cross-module communication**: reference another module's `Contracts` /
  `IntegrationEvents` projects only — never its Application or Infrastructure.
- **Central Package Management**: every dependency version lives in
  `Directory.Packages.props`; pins are only added alongside the feature that uses them.

## Verification loop

```bash
dotnet build modulus.slnx                                   # expect 0 warnings, 0 errors
dotnet test modulus.slnx --filter "Category=Unit"
dotnet pack modulus.slnx -c Release
```

> Note: `dotnet format --verify-no-changes` currently fails repo-wide on
> pre-existing CRLF issues; do not mass-reformat unrelated files.
