# Dual-Targeting Plan: net8.0 + net10.0

Derived from the 2026-07-26 NuGet-packaging review. Goal: every publishable
`src/` package builds and restores cleanly for **both** `net8.0` and `net10.0`
consumers, so the framework isn't limited to .NET 10-only apps. `cli/` is
explicitly **out of scope** — it stays `net10.0`-only.

Planning only — no implementation in this pass.

## Current state

Every `src/` project (~30 csproj files, confirmed by grep) hard-pins
`<TargetFramework>net10.0</TargetFramework>` via `build/Modulus.Common.props`.
`Directory.Packages.props` pins a single version — mostly `10.0.9`/`10.x` — for
every package, including ones whose major version is locked to the runtime
(ASP.NET Core shared-framework packages, EF Core design-time tooling).

One artifact of an earlier, half-finished attempt already exists:
`src/core/Modulus.AspNetCore/Modulus.AspNetCore.csproj` has a
`Condition="'$(TargetFramework)' == 'net8.0'"` branch for Swashbuckle that's
currently dead code, since the TFM is singular.

## Why this isn't a version-bump — it's per-package conditional versioning

ASP.NET Core / EF Core packages that ship as part of, or bind directly to, the
shared framework are versioned in lockstep with the runtime major version —
confirmed a `10.0.x` release of these cannot be referenced from a `net8.0`
project. Central Package Management (CPM) supports this via
`Condition="'$(TargetFramework)'=='net8.0'"` on `<PackageVersion>` entries, so
the fix is per-package, not a single global downgrade.

## Package-by-package scope

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

## Execution steps

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

## Open questions to resolve before implementing

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
