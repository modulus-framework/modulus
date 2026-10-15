# Authorization Conformance & Traceability Specification

**Version:** 0.1 (initial — seeded registry + suite design)
**Status:** Controlled artifact — change-controlled per BRS AS-09
**Date:** 2026-10-15
**Companion:** [`docs/IDENTITY_PLATFORM_IMPLEMENTATION_PLAN.md`](IDENTITY_PLATFORM_IMPLEMENTATION_PLAN.md)
(plan v1.3.1, §7 step 1 and §9)

This specification defines the automated conformance suite for the BRS §16.3 authorization truth
table (AUTH-01..AUTH-34) on Modulus, and the traceability registry that records each row's status.
The suite is part of the product: it is the acceptance evidence for the plan's phase exit criteria.

## 1. Purpose and control rules

1. **One fixture per AUTH row.** Every truth-table row maps to exactly one traceable fixture (or
   fixture group) in `Modulus.Testing.Authorization`; the fixture name and the row id appear in
   each other's traceability records.
2. **The suite is change-controlled.** Adding coverage is normal work; weakening expected outcomes
   or removing a fixture requires a BRS/ADR change-record reference in the change. "Making the
   implementation pass" is never a reason to weaken a fixture.
3. **Status flips only when a fixture runs.** A row's status never changes because of an
   inspection, a source search or an argument — only when its fixture executes (CI or locally).
4. **Standing caveat:** absence from a source search is evidence of a likely gap, not proof a
   capability is absent from every integration path. Seeded statuses below derive from the
   2026-10-15 source audit (file:line evidence in plan v1.3.1 §3/§4); each remains open to
   revision when its fixture is authored and first runs.
5. **Scenario text is transcribed, not paraphrased.** When a fixture is authored, the row's
   scenario summary column is replaced by the verbatim BRS §16.3 row text (or a direct reference
   to it), so the registry never drifts from the BRS.

## 2. Status model

| Status | Meaning |
|---|---|
| `unproven` | No fixture has run yet; framework *believed* to satisfy (or not satisfy) the row — belief is not evidence |
| `pass` | Fixture ran green in CI |
| `fail` | Fixture ran red; the owning work item (plan §4) is referenced |
| `partial` | Row is split across multiple behaviors with different statuses (used sparingly; the split is documented) |
| `not-applicable` | Row cannot apply to the Modulus shape; requires an ADR reference explaining why |

## 3. AUTH-row registry (seeded 2026-10-15)

Phase assignments follow the plan's exit tables (§7): P1a = AUTH-01–02, 04–14, 26, 30, 34;
P1b = AUTH-21–25, 27–29; P2 = AUTH-15–20, 31–32. Rows AUTH-03 and AUTH-33 were not assigned by
the earlier tables — assigning them (or marking not-applicable with an ADR) is part of the first
conformance-skeleton task.

| Row | Scenario (summary — verbatim BRS text lands with the fixture) | Seeded status | Basis (2026-10-15 audit) | Owning work item | Phase |
|---|---|---|---|---|---|
| AUTH-01 | *to be transcribed from BRS §16.3* | unproven | P1a basic-isolation area; tenant isolation engine exists | — (existing engine) | P1a |
| AUTH-02 | *to be transcribed* | unproven | deny-by-default engine exists | — (existing engine) | P1a |
| AUTH-03 | *to be transcribed* | unproven | **phase assignment open** | — | open |
| AUTH-04 | *to be transcribed* | unproven | P1a grant/scope area | — (existing engine) | P1a |
| AUTH-05 | *to be transcribed* | unproven | P1a grant/scope area | — (existing engine) | P1a |
| AUTH-06 | *to be transcribed* | unproven | P1a grant/scope area | — (existing engine) | P1a |
| AUTH-07 | *to be transcribed* | unproven | P1a grant/scope area | — (existing engine) | P1a |
| AUTH-08 | A required scope attribute missing on a record ⇒ explicit denial **and** a data-quality finding, without leaking record data | **fail** | `MISSING_ATTRIBUTE` reason and any finding sink verified absent; `METADATA_MISSING` exists only for "no policy for a type" (`Resources/AccessDecision.cs:80`) | A4 | P1a |
| AUTH-09 | Only a grant explicitly declaring missing-attribute coverage matches the missing-attribute case; ordinary wide grants never do | **fail** | No such grant declaration exists | A4 | P1a |
| AUTH-10 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |
| AUTH-11 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |
| AUTH-12 | Creating (or updating into) a record whose scope-defining references fall outside the caller's effective scope is blocked | **fail** | No creation/resulting-scope validation anywhere; `OUT_OF_SCOPE` exists only as a reason constant (`Resources/AccessDecision.cs:47`) | A3 | P1a |
| AUTH-13 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |
| AUTH-14 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |
| AUTH-15 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-16 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-17 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-18 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-19 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-20 | *to be transcribed* | unproven | P2 governance area | A13/A15/B2 | P2 |
| AUTH-21 | An unauthorized write to a classified field is rejected (never silently ignored), across create/update/patch/bulk/import | **partial** | `IFieldAuthorizer.AuthorizeWriteAsync` exists (`FieldAuthorizer.cs:16-44`) but is unwired — enforcement is call-site optional | A7 | P1b |
| AUTH-22 | A masked classified field never leaks through any endpoint, export, bulk path or related lookup | **partial** | No pipeline redaction or output safety net; AI-connector-only usage of the authorizer | A7 | P1b |
| AUTH-23 | A user-initiated job re-authorizes at execution; lost access stops it safely and audibly | **fail** | `JobEnvelope` carries tenant/correlation only; runner does no authorization (`ChannelJobQueue.cs:14-20`, `ProcessLoopAsync` :280-330) | A6 | P1b |
| AUTH-24 | System jobs run under named system identities with explicit grants; never inherit a person's authority | **fail** | No principal on jobs; no SystemJob identity | A6 | P1b |
| AUTH-25 | An agent acting for a user never exceeds the user's effective grants | **fail** | No agent/delegated-principal context exists | A9 | P1b |
| AUTH-26 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |
| AUTH-27 | *to be transcribed* | unproven | P1b export area | A5/A7 | P1b |
| AUTH-28 | *to be transcribed* | unproven | P1b bulk area | A3/A5/A7 | P1b |
| AUTH-29 | List, search, count, aggregate, dashboard and export surfaces all apply the caller's scope | unproven | Query filters + `ForExport` helpers exist; coverage across every surface is fixture work | A5 | P1b |
| AUTH-30 | A new permission is visible, ungranted by default, and announced to administrators | **fail** | No catalog lifecycle or change notice (`IPermissionRegistry.cs:20-27`); deny-by-default holds only the "ungranted" part | A10 | P1a |
| AUTH-31 | Cross-principal record access only through explicit valid shares; never across tenants | **fail** | No share primitives exist | A8 | P1b (build) / P2 (exit confirm) |
| AUTH-32 | *to be transcribed* | unproven | P2 area | A13/A15/B2 | P2 |
| AUTH-33 | *to be transcribed* | unproven | **phase assignment open** | — | open |
| AUTH-34 | *to be transcribed* | unproven | P1a area | — (existing engine) | P1a |

Seeded totals: 8 fail · 2 partial · 24 unproven — of the unproven rows, AUTH-03 and AUTH-33 also
await phase assignment. The `unproven` majority is
deliberate: the framework's authorization engine is mature, but this registry claims only what a
fixture has proven.

## 4. Suite design

### 4.1 Projects

- **`src/testing/Modulus.Testing.Authorization`** — packable library (standard repo conventions:
  `net8.0;net10.0`, CPM, `PublicAPI.Shipped/Unshipped.txt`, `TreatWarningsAsErrors`). Contains the
  test world, fixture base and per-row fixtures/assertions. References the authorization engine
  (`Modulus.Platform`, `Modulus.Authorization.EntityFrameworkCore`, `Modulus.Core`) and
  `Modulus.Testing`.
- **`tests/unit/Modulus.Testing.Authorization.Tests`** — harness self-tests (`[Trait("Category",
  "Unit")]`, `Modulus.Test.props`): the world builder produces the documented shape; fixtures are
  deterministic; a deliberately-broken engine change flips the expected fixture red (mutation
  check).

### 4.2 Test world (deterministic, shared)

One builder constructs the standard BRS-shaped world; every fixture consumes it (never bespoke
data):

- **Two tenants** (A, B) + the host context; one cross-tenant membership for the
  membership-isolation cases.
- **Two org nodes** in a parent/child relationship (org-subtree roll-up cases).
- **Two ownership values** (owner X, owner Y) for `Own`-scope cases.
- **One record with a missing required scope attribute** (A4/AUTH-08/09 cases).
- **Grant set:** one active direct grant, one expired grant, one role grant, one scoped grant
  (`Tenant`/`OrgUnit`/`Own`/`Assigned` each represented), one deny grant, one delegation.
- **Later additions:** dimension values + hierarchy (with A5), a share + masked reference (with
  A8), an agent context (with A9), a queued job (with A6).
- **Determinism:** fixed `TimeProvider` (no wall clock), no network, no shared mutable state;
  in-memory/SQLite stores per the `Modulus.Testing` swap conventions.

### 4.3 Fixture pattern

- One fixture class per AUTH row: `Auth{n}Fixtures` (e.g. `Auth12Fixtures`), each test method
  annotated with the row id and the behavior leg it proves (allow leg + deny leg + bypass legs).
- Engine-level rows assert against the decision engine + scope enforcer directly; HTTP-surface
  legs delegate to `SecurityProbeSuite` / `TenantIsolationAssertions`
  (`src/testing/Modulus.Testing/Security/`) through a hosted test app built from the same world.
- A failing fixture fails the phase exit criterion it belongs to; the registry's *Basis* column
  links the owning work item from plan §4.

### 4.4 CI and change control

- The suite runs on every engine change (CI) alongside `Category=Unit`.
- Registry updates land **in the same PR** as the fixture that justifies them.
- Weakening a fixture requires the BRS change-record id in the PR description; CI or reviewers
  reject silent weakening.

## 5. Status transition rules

```
unproven ──(fixture first runs)──► pass | fail
fail ──(owning work item lands)──► pass        (fixture re-run required)
partial ──(all legs proven)──────► pass | fail (per-leg results documented)
not-applicable ── only via ADR reference
```

- A `fail` row must reference its owning work item; a work item is not *done* until its rows are
  `pass` (plan §14 completion evidence).
- Phase exit = all rows of that phase `pass` (plan §7 exit criteria).

## 6. Relationship to per-app probes

The conformance suite proves the **engine**; per-app probes prove an **application's wiring** of
the engine. Both are required: `SecurityProbeSuite` (anonymous/foreign-tenant probes) and
`TenantIsolationAssertions` (per-context isolation) run against every generated app, and the
CLI's `generate-crud` scaffold ships an MC-05-style security test class per secured resource
(plan §9). A green conformance suite plus red app probes means a wiring defect, not an engine
defect — the fixture and probe results together locate the fault.
