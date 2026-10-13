# CLI review plan: module-owned wiring, Synvro.* rename, modulus-ui package refs

Status: planned, not implemented. Scope: `cli/` (generators, Scriban templates, tests), plus two small framework seams in `src/`.

## Context

Rule: everything module-specific lives inside the module's composition root (`{Module}Module`, in the module's Infrastructure project). That covers service registration, permissions and grants, integration events, GraphQL/gRPC/endpoint contributions, MVC application parts and seeding. The host `{App}.Api/Program.cs` only composes modules (`AddModulus(..., m => m.AddModule<X>())`) and sets up cross-cutting concerns: auth, CORS, rate limits, OpenAPI, correlation, the migrate call, and the base `AddModulusRealtime/GraphQL/Grpc/Webhooks/AiConnector` calls with their `Map*` calls.

Today the CLI breaks this rule. It patches host `Program.cs` per entity. The `Synvro.*` package rename is half done, and UI package references are added after scaffolding, which makes them fragile.

## Part A. Move module-specific wiring into the module

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

### Framework seams to add (in `src/`)

1. **Per-module event registration for Realtime and Webhooks.** Add public `services.AddRealtimeEvent<T>(audience, payload?)` and `services.AddWebhookEvent<T>(desc?, payload?)`. The builders' constructors are internal today (`src/messaging/Modulus.Realtime/RealtimeServiceCollectionExtensions.cs:14,93`; `src/messaging/Modulus.Webhooks/WebhooksServiceCollectionExtensions.cs:24,77`), so a module has to pass the whole configuration. New public API goes in `PublicAPI.Unshipped.txt`.
2. **Module-contributed assembly registries.** Add `services.AddModulusEndpointAssembly(asm)`, consumed by `MapModulusEndpoints()` when called with no arguments (`src/core/Modulus.AspNetCore/Endpoints/EndpointDiscovery.cs:29`). The gRPC equivalent goes next to `MapModulusGrpc` (`src/platform/Modulus.Grpc/Server/GrpcServerExtensions.cs:84`). GraphQL already supports this through `AddGraphQLContributor<T>` (`src/platform/Modulus.GraphQL/GraphQLServiceCollectionExtensions.cs:128`), so the CLI only needs to use it. Keep the explicit-assembly overloads.
3. **AI connector:** no seam needed. It reads handler registrations lazily, and the module's `AddMediatorHandlers` already registers the capabilities.

Ordering is safe. Module `ConfigureServices` runs inside `AddModulus` before the host's `AddModulusX` calls, and Realtime and Webhooks run their `configure` callback on every call.

### CLI changes

- Add a `ModuleWiring` helper that edits `{Module}Module.cs` idempotently and is CRLF-aware, following the existing `ProgramEditor` patterns. Repoint `ApiPermissionWiring`, `AiWiring.EnsureIndexerGrant`, `RealtimeWiring`, `WebhooksWiring`, `GraphQLWiring`, `GrpcWiring` and `GenerateCrudCommand` at it.
- `Module.sbn`: add the permission and grant block, `AddControllers().AddApplicationPart`, endpoint, GraphQL and gRPC assembly registration, and an `InitializeAsync` stub for seeding. Update the `Endpoint.sbn` header comment (lines 7-9), which tells users to grant roles "in Program.cs".
- `Program.sbn`: remove the example-module permissions (159-167) and the Presentation DLL scans. Call `MapModulusEndpoints()` with no arguments.
- `identity/IdentityModule.sbn`, `tenancy/TenancyModule.sbn`, `audit/AuditModule.sbn`: move seeding into `InitializeAsync`. Keep resolvers and OpenIddict in the host as cross-cutting concerns.
- Existing apps that are wired in the host keep working. New commands should detect wiring in either place so they do not duplicate it.
- Tests that assert `Program.cs` content need updating. They are in `tests/unit/Modulus.Cli.Tests/`: `ApiPermissionTests`, `AiCommandTests`, `RealtimeCommandTests`, `WebhooksCommandTests`, `GraphQLCommandTests`, `GrpcCommandTests`, `AuditStoreCommandTests`, `IdentityBackendTests`, `MultiTenancyTemplateTests`. Also check `UiAccessGates.GrantCall` and the `Ui*Tests` files.

## Part B. Finish the Synvro.* rename

These still use the old IDs:
- `cli/Commands/GenerateGrpcCommand.cs:70` (`Modulus.Grpc`)
- `cli/Services/GraphQLWiring.cs:14`, `RealtimeWiring.cs:12`, `AiWiring.cs:13,14,17`
- `cli/Services/VersionCheckService.cs:12,79` (should be `Synvro.Modulus.Cli`, so the self-update check finds the tool)
- The `--framework-only` filters in `OutdatedCommand` and `UpdateCommand`
- `ProjectFileService.cs:~106` (framework-version detection)
- Messages in `NewAppCommand.cs:645` and `Models.cs:123,127`

`AddBffCommand.cs:82` and `DoctorCommand.cs:248` use substring checks and probably work, but verify them. Grep the templates for unprefixed `Include="Modulus.`.

## Part C. modulus-ui package references (0.9.0)

Seven libraries plus `Synvro.Modulus.Ui.Templates`, all at 0.9.0, all from `E:\Personal\framework\modulus-ui`.

- `cli/Templates/app/web.csproj.sbn`: add `Synvro.Modulus.AspNetCore.Mvc`, `Ui.Abstractions`, `Ui.Assets` and `AspNetCore.Hosting` at `{{ui_kit_version}}`. That variable is already passed at `NewAppCommand.cs:445` but no template uses it. Keep `AddUiFrameworkPackages` (around line 633) as a manifest-driven top-up.
- When UI packages can't be resolved for a web kind, fail with an error at `NewAppCommand.cs:645` instead of a warning.
- `UiFrameworkPackages.LocalFeed()`: also try `~/.modulus/nuget-feed` and `../modulus-ui/nupkg`.
- Single source of truth for versions: `UiTemplatePackage.DefaultVersion` (0.9.0) against `ui-kit.json`, and `FrameworkVersion` (1.4.0) against `build/Modulus.Packaging.props`. Add a test that they agree.
- MVC engine gap: `Program.Web.sbn` needs `AddControllersWithViews()` and `MapControllers()` when the engine is mvc. Without these, the MVC controllers are unreachable (AGENTS.md open defect).

## Part D. Housekeeping

- Delete the stray `cli/Commands/NewAppCommand.cs.bak`.
- Fix the mojibake in `NewAppCommand.cs` comments.
- Update AGENTS.md: add the module-owned wiring rule, correct `--kind api|web` to `api|webapp+api` (lines around 159 and 848), correct the samples line (around 843) to `Synvro.Modulus.Ui.*` 0.9.0, and add `ui create-crud` and `ui add-assistant` to the command list.

## Verification

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

## Assumptions and open questions

- `samples/` is out of scope and keeps its current UI 1.4.0 references.
- Blazor and Fluid web hosts stay unsupported, because modulus-ui itself has no app host for them yet.
- Backward compatibility: apps already wired in the host keep working.
- The framework seams in Part A touch `src/` and need their own review and PublicAPI updates.
