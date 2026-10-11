# Production readiness: review findings and plan

**Status (2026-10-11): in progress, not production-ready.** Step 0 (plan-doc cleanup) is done. In step 1 the build and
formatting are green; two test items (T1, T2) are open. Steps 2–5 are open. Tick each box when its fix and test land. Set this status to "complete" once every gate in
*Verification* passes.

Scope: everything in the repository except `samples/`.

Decisions taken:
- The unused Phase 5–9 CLI work is deleted from this repository. UI work belongs in `modulus-ui`.
- Security fixes that change defaults ship as **breaking, secure-by-default** changes. Each is recorded in `CHANGELOG.md` together with its opt-out.

## Gate results

| Gate | Before (review) | Now |
|---|---|---|
| `dotnet build modulus.slnx -c Release` | **FAIL**: 36× RS0016 in `cli/Services/FormStateService.cs` | **PASS**: 0 warnings, 0 errors |
| `dotnet format modulus.slnx --verify-no-changes` | **FAIL** (same compile errors, plus 41 whitespace violations in the CLI) | **PASS** after `dotnet format whitespace` on the CLI and its tests |
| `dotnet test modulus.slnx --filter Category=Unit` | not runnable | **1,701 passed, 6 failed**: see T1 and T2 |
| `dotnet list package --vulnerable --include-transitive` | PASS, none | PASS |
| TODO / FIXME / `NotImplementedException` in `src/` | none | none |

## Step 0: Plan docs ✅

- [x] This document created and linked from `AGENTS.md` ("Production readiness (open work)") and `PROJECT_STRUCTURE.md`.
- [x] Deleted as obsolete:
  - `cli/PHASE_*.md` (28 files) and `cli/CLI_PRO_SCAFFOLDING.md`, which claimed a "v3.5.0" release (the real version is 1.4.0, `build/Modulus.Packaging.props`);
  - `docs/UI_FRAMEWORK_PLAN.md` and `docs/UI_FRAMEWORK_GUIDELINE.md`, which described the removed `src/ui/`.
- [x] Archived as completed in `docs/archive/`, each with a status banner: `IMPROVEMENT_PLAN.md`, `DUAL_TARGETING_PLAN.md` and `ROADMAP_TIER3.md`. Links updated.
- [x] Status lines corrected:
  - `docs/ERP_IDENTITY_GAPS_PLAN.md`: complete, open gaps listed.
  - `docs/APP_KIND_SPLIT_AND_IDENTITY_UI_PLAN.md`: Phase A complete; Phase B superseded and moved to `modulus-ui`.
- Still live: `docs/ADVANCED_FEATURES_PLAN.md` (Modulus.UI.AI, push notifications) and `docs/SECURITY_STAGE2_PLAN.md` (2 items).

## Step 1: Unblock the build ✅

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
- [ ] **T1.** Five CLI tests labelled `Category=Unit` depend on the machine's template cache or the network:
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
- [ ] **T2.** `ApprovalWorkflowTests.Two_approvers_racing_for_one_step_cannot_both_win` is flaky. It failed once in the full
  parallel run with `SQLite Error 5: unable to delete/modify user-function due to active statements`, and passed 3/3 alone.
  Fix: give the test its own connection per approver and register SQLite functions before any statement runs, or put
  the test in a non-parallel collection.

## Step 2: Security (identity, platform, AI)

### Critical
- [ ] **S1. Password-reset mail goes to the typed address, not the stored one.**
  - Where: `src/identity/Modulus.Identity/ModulusAccountController.cs:68-70` (also `:176-178`); `IdentityEmailQueue.cs:38,62`.
  - Problem: `FindByEmailAsync` matches through the upper-cased normalized form, so a lookalike address (`admin@gıthub.com`, dotless ı) finds the victim's account. The token is then mailed to the attacker's address: account takeover.
  - Fix: queue only the user id; the worker sends to the stored `user.Email`. Do the same for the confirmation mail.
  - Test: a lookalike address never receives the mail.

### High
- [ ] **S2.** `POST account/2fa/setup` replaces the live authenticator key without re-authentication (`UserTwoFactorService.cs:45-58`, `ModulusAccountController.cs:272-275`). A stolen session can take over the second factor.
  Fix: refuse while two-factor is enabled. The owner disables it first, which requires the password.
- [ ] **S3.** `/connect/introspect` checks only the signature and lifetime (`ModulusTokenController.cs:636-669,736-789`). Revoked tokens, tokens of ended sessions and `id_token`s come back `active=true`.
  Fix: check the OpenIddict token entry status, the session registry (`sid`) and the token type (access tokens only).
- [ ] **S4.** Open redirect through control characters (`SamlSignInService.cs:289`, `src/platform/Modulus.Bff/Endpoints/BffEndpointRouteBuilderExtensions.cs:136`).
  `/%09/evil.com` passes the check, and the browser strips the tab and lands on `//evil.com`.
  Fix: one shared helper that mirrors ASP.NET `IsLocalUrl` (rejects control characters), used in both places.
- [ ] **S5.** SAML and federated sign-in skip the account's local second factor, and `LinkExistingByEmail` trusts an IdP email that may be unverified (`SamlSignInService.cs:143-146`, `ExternalAccountProvisioner.cs:51-59`, `FederatedLoginValidator.cs:50-75`).
  Fix: enforce local 2FA as the LDAP path does, and link by email only when the provider is flagged as asserting verified addresses.
- [ ] **S6.** `TenantMiddleware.cs:44-90` checks only `ctx.User` from the default scheme. Membership is not checked in two cases:
  - endpoints that use their own authentication scheme (the BFF client schemes);
  - hosts where `UseMultiTenancy` runs before `UseAuthentication`.

  Fix: re-check membership after endpoint authentication (an authorization requirement or endpoint filter), and refuse the wrong middleware order at startup.

### Medium
- [ ] **S7.** The BFF bearer policy accepts any token from the authority when `Audiences`, `AllowedClientIds` and `ClientId` are all empty (`BffServiceCollectionExtensions.cs:212`). Fix: fail at startup. *(breaking)*
- [ ] **S8.** The BFF cookie uses `SecurePolicy = SameAsRequest` (`:236`). Fix: `Always` outside Development. *(breaking)*
- [ ] **S9.** The refresh grant fails open when no user manager resolves (`ModulusTokenController.cs:402-407`). Fix: deny.
- [ ] **S10.** `2fa/disable` uses `CheckPasswordAsync`, which does not count toward lockout (`ModulusAccountController.cs:297`). Fix: `CheckPasswordSignInAsync(lockoutOnFailure: true)`.
- [ ] **S11.** LDAP binds before checking local lockout and does not count failures (`LdapPasswordGrantValidator.cs:64-80`).
- [ ] **S12.** `/bff/login` has no rate limit unless one is configured (`BffServiceCollectionExtensions.cs:341`). Fix: a default sensitive-route limiter.
- [ ] **S13.** Insecure defaults, changed as breaking:
  - `Storage:IsolateTenants` is now `true` when multi-tenancy is registered (`StorageOptions.cs:19`);
  - `TrustPermissionClaims` is now `false` (`ModulusAuthorizationOptions.cs:21`).
- [ ] **S14.** `EfPermissionGrantStore.GetGrants` runs synchronous `.ToList()` queries on every authorization check (`:41-79`, called from `PermissionRequirement.cs:74`). Fix: an async path plus a per-request cache.
- [ ] **S15.** The reset-mail queue drops writes silently when full (`IdentityEmailQueue.cs:32-36`, capacity 1000). Fix: log a warning and count drops with a meter.
- [ ] **S16.** Lower priority:
  - `RedisIdempotencyStore.cs:52,99` replays a response when the stored fingerprint is empty;
  - `ShopFloorServices.SetPinAsync` has no tenant check of its own;
  - `LocalFileStorage.cs:34` compares paths with `OrdinalIgnoreCase`, which is wrong on Linux.

Tests: `tests/unit/Modulus.Identity.Tests`, `Modulus.Bff.Tests`, `Modulus.Platform.Tests`.

## Step 3: Data and messaging (High)

- [ ] **D1.** Mongo `DeleteRangeAsync` drops `Skip`/`Take` on its server-side paths (`src/data/Modulus.Data.MongoDB/MongoRepository.cs:203-218`). "Delete the oldest 100" deletes every match.
  Fix: when paging is set, resolve the ids through `ListAsync`, then `DeleteMany` with `_id $in`.
- [ ] **D2.** The Mongo inbox starts without its unique index `ux_eventid_handlername` when creating it fails (`InboxMongoExtensions.cs:94-97`). Concurrent consumers then process an event twice.
  Fix: fail startup, with an explicit `SkipIndexCreation` opt-out.
- [ ] **D3.** The `OutboxDepth` gauge accumulates (`OutboxProcessor.cs:33`, `MongoOutboxProcessor.cs:31`). `_lastReportedDepth` lives on processors registered as scoped, so it is always 0.
  Fix: move it to a singleton tracker.
- [ ] **D4.** RabbitMQ defaults to `guest`/`guest` and has no TLS option (`RabbitMqOptions.cs:10-13`, `RabbitMqEventBus.cs:185-197`, `RabbitMqEventConsumer.cs:87-94`).
  Fix: validated options with required credentials, and an `Ssl` section. *(breaking)*
- [ ] **D5.** One unexpected Kafka error stops the host (`KafkaEventConsumer.cs:175-181,216,265`).
  Fix: catch per iteration, back off, and close the consumer in `finally`. Reuse one dead-letter producer (`:298-306`) instead of building one per message.
- [ ] **D6.** The Mongo repository scans whole collections client-side without a document filter, and ignores the spec's sort when a filter is set (`MongoRepository.cs:84-93,136-138`).
  Fix: always apply the sort before `Skip`/`Take`, and add a `MaxClientSideScan` guard.

Tests: `tests/unit/Modulus.Data.MongoDB.Tests`, `Modulus.Outbox.Tests`, `Modulus.EventBus.*.Tests`. For D1, D2 and D6, also run the Mongo integration tests.

## Step 4: Medium (core, messaging, CLI)

- [ ] **M1.** `IdempotencyStoreSweeper.cs:64-68` has a bare `catch {}` and no logger. Fix: log a warning.
- [ ] **M2.** `InMemoryIdempotencyStore` has no entry limit, and `CompleteAsync`/`AbandonAsync` don't check that the caller owns the claim (`:55-81`).
  Fix: add a claim token and a `MaxEntries` option.
- [ ] **M3.** `EfInboxStore.cs:263-266,306-309` catches every `InvalidOperationException`. Fix: narrow the catch to the in-memory provider.
- [ ] **M4.** RabbitMQ consumer and publisher:
  - reject `AutoAck=true` combined with manual ack/nack;
  - log an error when there is no dead-letter exchange;
  - pass the `CancellationToken` through;
  - reuse publisher channels (`RabbitMqEventBus.cs:60-62`).
- [ ] **M5.** `SqlServerRowLevelSecurity.EnsureAsync` drops and recreates policies outside a transaction (`:149-169`). Fix: use a transaction, or `ALTER SECURITY POLICY`.
- [ ] **M6.** The Mongo outbox poller has no drain loop and no leader election, and swallows OCE (`MongoOutboxPollingService.cs:31-48`). Fix: mirror the EF `OutboxPollingService`.
- [ ] **M7.** `src/observability/.../OpenTelemetryExtensions.cs:35` is a public no-op placeholder. Fix: implement it, or mark it `[Obsolete]`.
- [ ] **M8.** CLI `TemplatePackageResolver.cs`:
  - the download URL is wrong for NuGet v3 feeds (`:202`); fix: use the flat-container URL;
  - downloads are cached with no integrity check; fix: verify the download is a valid package;
  - `catch {}` at `:214`; fix: report the error.

## Step 5: Wrap-up

- [ ] `CHANGELOG.md`:
  - a Security section (S1–S6);
  - the breaking defaults (S7, S8, S13, D4) with their opt-outs;
  - the other fixes.
- [ ] Tick every box above and set the status to "complete".

## Verification

- `dotnet build modulus.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet format modulus.slnx --verify-no-changes`: clean.
- `dotnet test modulus.slnx --filter "Category=Unit"`: green. Also run the net8 leg on the touched projects (`-p:ModulusTestTfm=net8.0`).
- `dotnet list modulus.slnx package --vulnerable --include-transitive`: none.
- Integration tests (Docker or Podman) for Mongo, the outbox and RLS.
- `dotnet pack modulus.slnx -c Release`, then generate an `api --auth openiddict --multi-tenancy` app from the local feed and run its tests, including `SecurityProbeTests`.
