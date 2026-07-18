# Tier 3 Roadmap — Remaining API/Platform Robustness

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

## Shared conventions (read once)

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

### End-to-end validation loop (run for every item)

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

## 1. Integration-test harness — ✅ DONE

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

## 2. Feature flags — ✅ DONE

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

## 3. Secrets management — ✅ DONE

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

## 4. PII encryption — ✅ DONE

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

## Definition of done (per item)

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
