# Session Memory — Modulus framework

## Objective
- Remove the `DependsOn` module-dependency mechanism entirely; replace with explicit module registration (`AddModulus(configuration, modules => modules.AddModule<T>()...)`) where registration order is authoritative for all lifecycle phases (PreConfigure → Configure → PostConfigure → InitializeAsync; ShutdownAsync reverse). Update samples, CLI templates, docs; run full verification.

## Important Details
- Design decisions (user-confirmed): full DependsOn removal; `{App}HostModule` deleted from CLI templates (Program.cs is the composition root); `GET /health/graph` repurposed to ordered module inventory `[{ name, type, initOrder }]`.
- **Packaging pipeline for samples**: samples consume framework via packed nupkg local feed, NOT project refs.
  - TradeFlow feed: `samples\TradeFlow\nupkg` (per its NuGet.config). Repack: `dotnet pack modulus.slnx -c Release -o samples\TradeFlow\nupkg` from repo root. All packages at uniform **1.3.0**.
  - Repo feed for CLI e2e: `nupkg\` at repo root (`dotnet pack modulus.slnx -c Release -o nupkg`).
- **Same-version repack trap (critical)**: bumping package content without bumping version means restore no-ops. Must clear: (1) global-packages folder for the package, (2) `dotnet nuget locals http-cache --clear` (v3-cache caches local-feed nupkgs!), and kill lingering `testhost` processes that LOCK the DLLs (silent Remove-Item failures). If still stale, `dotnet nuget locals all --clear`. Zip extraction preserves entry mtimes — an extracted DLL dated at build time (not extraction time) means old nupkg.
- TradeFlow module wiring order (16, authoritative): Identity, Configuration, Tenants, Notifications, Vendors, OrgStructure, Budgeting (`BudgetsModule` class), Customs, Procurement, TradeFinance, Import, Inventory, Costing, Finance, VirtualFileExplorer, WorkflowEngine. Features + SpendAnalysis deliberately unwired.
- `ModulusBuilder.AddModule` duplicate registration = idempotent no-op (skip), not a throw. Each module type instantiated exactly once; `Complete()` (called by AddModulus) runs the 3 phases in registration order.
- Build has **5 pre-existing MSB9008 warnings** (orphaned test projects: Caching.Redis, EventBus.Kafka, EventBus.RabbitMQ, Inbox.MongoDB, Outbox.MongoDB) — not regressions.
- Env notes: `rg` unavailable (use `grep` tool or `Select-String`); grep tool can return stale results after file deletion — verify with `Test-Path`.
- Historical docs intentionally unchanged: `IMPROVEMENT_PLAN.md`, `samples\TradeFlow\PHASE1_*.md`, `docs\BRS\BRS-Phasing-Implementation.md`, `docs\PROCUREFLOW_PLAN.md`.
- Work committed by user as f3fe22b ("feat: CLI outdated/update, module system cleanup, docs rebrand, TradeFlow expansion"); later fixes (this session's template/testing bug fixes) are in working tree, NOT committed.

## Work State
### Completed — everything; task is done
- Core rework: deleted `DependsOnAttribute.cs`, `ModuleGraph.cs`, 3 dependency exceptions; rewrote `IModule.cs`, `ModulusModule.cs`, `ModuleContext.cs` (`ModuleDescriptor`: Name, ModuleType, InitOrder), `IModuleLoader.cs`, `ModuleLoader.cs`, `ModulusBuilder.cs`.
- AspNetCore: `AddModulus(IServiceCollection, IConfiguration, Action<ModulusBuilder>)` in `ModulusServiceCollectionExtensions.cs`; `UseModulus` zero-modules warning updated.
- Observability: `ModuleGraphEndpoint.cs` → ordered inventory (reads `IModuleLoader.GetDescriptors()`).
- CLI: `HostModule.sbn` deleted; `Program.sbn`/`AppTests.sbn` rewritten; `Module.sbn` doc updated; `NewAppCommand.cs`, `AddModuleCommand.cs` (`WireModuleRegistration`, anchor `AddModulus(`), `ModuleDiscovery.cs` probe `("Modulus modules", "AddModulus(")`.
- TradeFlow: explicit 16-module registration in Program.cs; host module deleted; feed repacked; build 0/0; unit + E2E (2/2) tests green.
- Docs: AGENTS.md, README.md, PROJECT_STRUCTURE.md, CHANGELOG.md (breaking entry + e2e-fixes entry), website/docs (module-system.md and api/core/module.md rewritten; service-lifecycle.md, api/index.md, cli/add-module.md, cli/templates.md, cli/app.md, quick-start.md, observability/overview.md, opentelemetry.md patched).
- **E2E verification found & fixed 3 real pre-existing bugs** (documented in CHANGELOG "Fixed — CLI template & testing-harness bugs"):
  1. `cli\Templates\app\NuGet.config.sbn` — `--package-source` inside XML comment = invalid XML → every generated app failed restore. Reworded.
  2. `cli\Templates\app\Program.sbn` — missing 9 namespace imports (Correlation, Cors, FeatureFlags, HealthChecks, Idempotency, OpenApi, RateLimiting, Security, Versioning) → generated app didn't compile. Added.
  3. `src\testing\Modulus.Testing.Architecture\ModuleBoundaryRules.cs` — scanned only `Modulus.*` assemblies (never app assemblies) + `typeof(IModule)` bound to a local placeholder interface (line 53) that nothing implements → `FindModuleTypes()` always empty, app events never name-checked. Rewritten: all non-dynamic assemblies, ReflectionTypeLoadException-safe (`OfType<Type>()` — avoids CS8619), skips abstracts (IntegrationEventBase false positive), real `Modulus.Core.Abstractions.IModule`.
- **Final verification — all green**:
  - `dotnet build modulus.slnx` — 0 errors, 5 pre-existing warnings.
  - `dotnet test modulus.slnx --filter "Category=Unit"` — 0 failures (incl. fixed `TemplateRenderingTests` — now asserts `AddModulus(builder.Configuration, modules =>` / `modules.AddModule<CatalogModule>()` / no "HostModule"; and Testing.Architecture).
  - `dotnet format modulus.slnx --verify-no-changes` — exit 0.
  - CLI e2e: packed CLI → global tool reinstall (uninstall + `dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli`) → `modulus app VerifyApp3 --force -q` + `modulus add-module Orders --force -q` (must run add-module from app root) → patched generated NuGet.config with modulus-local → app builds 0/0 → **5/5 generated tests pass** (boot, /health/live, POST→GET round-trip, 2 boundary tests). Temp apps cleaned up.

### Active
- (none)

### Blocked
- (none)

## Next Move
- Nothing pending for this task. Possible follow-ups if user asks: (a) repack `samples\TradeFlow\nupkg` Testing.Architecture to give TradeFlow the fixed ModuleBoundaryRules (TradeFlow doesn't call it today; its tests pass as-is); (b) address the 5 orphaned MSB9008 test projects; (c) commit the working tree (user commits explicitly only); (d) TradeFlow NuGet.config has a stale comment referencing ProcureFlow pack path.

## Relevant Files
- `src\core\Modulus.Core\{Abstractions\IModule.cs, Abstractions\ModulusModule.cs, Abstractions\ModuleContext.cs, IModuleLoader.cs, ModuleLoader.cs, ModulusBuilder.cs}` — rewritten.
- `src\core\Modulus.AspNetCore\Extensions\ModulusServiceCollectionExtensions.cs` — new AddModulus overload.
- `src\observability\Modulus.Observability\ModuleGraphEndpoint.cs` — ordered inventory.
- `src\testing\Modulus.Testing.Architecture\ModuleBoundaryRules.cs` — fixed scanning + placeholder removal.
- `cli\Templates\app\{Program.sbn, NuGet.config.sbn, AppTests.sbn}` — rewritten/fixed; `cli\Templates\module\Infrastructure\Module.sbn`; `cli\Commands\{NewAppCommand.cs, AddModuleCommand.cs}`; `cli\Services\ModuleDiscovery.cs`.
- `tests\unit\Modulus.Cli.Tests\TemplateRenderingTests.cs` — updated assertions; `tests\unit\Modulus.Core.Tests\`, `tests\unit\Modulus.AspNetCore.Tests\AddModulusRegistrationTests.cs` — rewritten.
- `samples\TradeFlow\src\API\TradeFlow.Api\Program.cs` — explicit 16-module registration; `...\Modules\ProcureFlowHostModule.cs` deleted; `samples\TradeFlow\nupkg\` — all Cobytelabs.Modulus.* 1.3.0.
- `AGENTS.md`, `README.md`, `PROJECT_STRUCTURE.md`, `CHANGELOG.md`, `website\docs\**` — updated.
- `nupkg\` (repo root feed) — all packages 1.3.0 incl. fixed CLI + Testing.Architecture.
