# ProcureFlow Phase-1 MVP — Implementation Plan

Status: **Approved** — plan of record for delivering the ProcureFlow BRS (v2.0) Phase-1 MVP on top of ModulusSampleErp.

Scope decisions (confirmed with stakeholders):

- **Full Phase-1 MVP** (M0–M5, all BRS bounded contexts, ~8-month program).
- **Extend in place + add new modules** — evolve existing ModulusSampleErp modules; add new `ModulusSample.Modules.*` modules alongside.
- **RLS deferred** to the M5 hardening phase; until then tenant isolation is enforced by `ModuleDbContext` query filters + a 2-tenant isolation integration test.
- **API-only** deliverable (Next.js UI is explicitly out of BRS scope); demo via integration tests / OpenAPI.

Source documents: `samples/ProcureFlow/BRS-Core.md`, `BRS-Business-Rules.md` (~150 phased rules), `BRS-Phasing-Implementation.md`. Rule IDs below (`BR-*`) refer to the business-rules register; every P1 rule must map to ≥1 test carrying the rule ID in its name.

---

## 1. Strategy

ModulusSampleErp remains the host application. Its platform layer is reused as-is:

- Identity (OpenIddict, roles/permissions, sessions), Tenants, Settings, Features, Notifications, Media
- Authorization with delegation + SoD enforcement
- Outbox/inbox per module, RabbitMQ event bus, Rebus sagas
- `Modulus.Testing` integration harness (`ModulusWebAppFactory` with per-context SQLite swap)
- Data topology already matches BRS D-02 / §5.6: **one PostgreSQL database, one schema per module**, shared connection key `"Database"`.

### Module map (BRS bounded context → action)

| BRS context | Action | Schema |
|---|---|---|
| Platform (org, numbering) | **NEW** `Platform` module: effective-dated org tree (Company/BU/Site/Dept/Position), position occupancy, NumberSequence service, FX rate service (dated, customs-vs-booking), tenant reference-data deltas | `platform` |
| Workflow engine | **NEW** `Workflow` module (BRS bundles it into Platform; split for modularity — logged deviation) | `workflow` |
| Notification engine | **EXTEND** `Notifications`: severity→channel map, en/bn templates, delivery log, mandatory rules, zero-recipient alerts | existing |
| Document vault | **EXTEND** `Media`: doc types, versioning, retention, source+confidence tags | existing |
| Vendors | **EXTEND** `Partners`: lifecycle, KYC, banks (maker-checker + `[ProtectedPersonalData]`), qualifications, scorecards, duplicate detection | `partners` |
| Budgeting | **NEW** `Budgeting` | `budget` |
| Sourcing | **NEW** `Sourcing` | `sourcing` |
| Procurement | **EXTEND** `Purchasing` (PR/PO/GRN exist): import-PO fields, feasibility gate, revisions, blanket PO, QC stages, budget hooks | `purchasing` |
| AP / 3-way match | **NEW** `Finance` (sales-side `Billing` stays untouched) | `fin` |
| Customs & Duty | **NEW** `Customs` | `customs` |
| Import Logistics | **NEW** `Imports` | `import` |
| Trade Finance | **NEW** `TradeFinance` | `tradefin` |
| Costing | **NEW** `Costing` | `costing` |
| Inventory valuation | **EXTEND** `Inventory`: batches (= import file no), WA revaluation, append-only value ledger | `inventory` |
| Intelligence | **NEW** `Intelligence` (implements engine seams, consumes events) | `intel` |
| Reporting | **NEW** `Reporting` (projections + 5 canned reports) | `mart` |

### Dependency graph (`[DependsOn]` additions wired into `ModulusSampleHostModule`)

```
Platform ← Workflow ← (all approval-using modules)
Partners / Budgeting / Customs → Platform
Sourcing → Partners
Purchasing → Partners, Budgeting, Sourcing, Workflow
Imports   → Purchasing, Customs
TradeFinance → Imports, Partners
Costing   → Imports, Customs, Inventory
Finance   → Purchasing, Costing, TradeFinance
Intelligence / Reporting → event subscribers only (no hard deps)
```

### Cross-module seams (Shared kernel, per BRS §5.3)

- Strongly-typed ids in `Shared.Domain`: `VendorId`, `PoId`, `PrId`, `FileId`, `LcId`, `BoeLineId`, `BudgetId`, `CostSheetId`, …
- Engine/query seams in `Shared.Application`: `IFeasibilityEngine`, `ICostForecastEngine`, `ISupplierRiskProvider`, `IDutyCalculator`, `IContextGateway`.
- **No cross-DbContext navigations, ever** — integration only via integration events or seam queries.

---

## 2. Milestones

### M0 — Baseline & skeleton (weeks 1–2)

1. Verify clean build of `ModulusSampleErp.slnx` (PHASE1_STATUS lists stale platform-module compile errors — confirm resolved).
2. Add real EF migrations to the six `EnsureCreated`-only modules (Catalog, Partners, Inventory, Sales, Purchasing, Billing); register in `MigrationExtensions.ApplyMigrations()`.
3. Shared kernel: typed ids + engine seams above; extend `Shared.Domain.AppPermissions` with ProcureFlow capability groups.
4. Scaffold `Platform` module with the first vertical slice: Company/Site CRUD + numbering (`IMP-{CO}-{FY}-{SEQ}`) proven by an integration test.

**Exit**: solution builds 0 warnings; `dotnet test` green; Platform vertical slice passes end-to-end over HTTP.

### M1 — Platform foundation (weeks 3–8)

- **Platform module**
  - Org tree: effective-dated versions (BR-ORG-01/02/03); sites with customs attributes (BR-ORG-05); position occupancy + union of permissions (BR-ORG-06/07).
  - NumberSequence: concurrency-safe, FY-aware.
  - FX rate service: dated rates, customs-vs-booking distinction (G11, BR-FIN-06).
- **Workflow module** (critical path, test-first)
  - Versioned JSON definitions; instances pin the version forever (BR-WF-01).
  - Step types v1: approval-chain(DoA), condition, system-action, timer (BR-WF-03).
  - Resolvers: position / manager-of / head-of / DoA — with stored snapshots (BR-WF-04).
  - Decisions: Approve / Reject / Return / Reassign / Request-info (BR-WF-05); atomic claim (BR-WF-06).
  - Return → Resubmit re-resolves only the returned slab (BR-WF-02).
  - Creator ≠ approver enforced (BR-PM-03, proven by test).
  - SLA hosted scanner at 50%/80% + escalation (BR-WF-08).
  - Simulate mode + publish gate (BR-WF-10); append-only `workflow_event` (BR-WF-11).
  - Seed default DoA matrix from BRS-Core §4.4.
- **Notifications extension**: BR-NTF-01/02/04/05/06/07/08/09 — severity→channel map, en/bn templates, inbox-deduped event subscription, mandatory rules, delivery receipts.
- **Media extension**: document vault with versioning + retention + `source|confidence` tags (feeds OCR later).

### M2 — Procurement core (weeks 9–16)

- **Partners → Vendors**: lifecycle (BR-VEN-01); Active-only on PO (BR-VEN-02); duplicate detection + merge (BR-VEN-03); KYC by vendor type (BR-VEN-04); bank accounts maker-checker + `[ProtectedPersonalData]` encryption (BR-VEN-05); category qualification with expiry job (BR-VEN-06); blacklist (BR-VEN-07); monthly scorecards (BR-VEN-08/09).
- **Budgeting**: FY(Jul–Jun) × cost-center × category budgets (BR-BUD-01); append-only reserve→commit→consume ledger (BR-BUD-02/05); revisions via Workflow (BR-BUD-03); hard/soft blocks (BR-BUD-04).
- **Sourcing**: RFQ from PR lines; AVL enforcement (BR-SRC-02); sealed bids (BR-SRC-03); landed-cost normalization via `IDutyCalculator` seam (BR-SRC-05); frozen bid tab (BR-SRC-07); not-lowest justification + CFO co-approval (BR-SRC-06); split awards → PO draft (BR-SRC-09).
- **Purchasing → Procurement**:
  - PR budget gate (BR-PR-02/05/06); DoA routing via Workflow.
  - PO domestic/import/service + import mandatory fields (BR-PO-02/08); feasibility gate with immutable snapshot + CFO override (BR-PO-03); revisions + re-approval >5% (BR-PO-04); budget commit (BR-PO-05); auto-close (BR-PO-06).
  - Blanket PO + call-offs + maverick flag (BR-CNT-01/02).
  - GRN stages + QC + tolerances + GR/IR accrual + release-order gate (BR-GRN-01..08, BR-IMP-05).
- **Finance (AP)**: invoice capture channels (BR-INV-01); line-level 3-way match + tolerances + exception queue (BR-INV-02/03); duplicate check (BR-INV-04); CI reconciliation (BR-INV-05); credit-note maker-checker (BR-INV-07); payment proposal + instruction export; **no bank execution** (BR-FIN-05).

### M3 — Import + trade finance spine (weeks 17–24)

- **Customs** (built first — Imports depends on it):
  - HS master effective-dated (BR-HS-01); item↔HS mapping maker-checker (BR-HS-02/05); duty-rate dated rows + exclusion constraint + maker-checker (BR-DS-01/02).
  - Duty cascade engine (BRS §23.1: AV, CD/RD/SD/VAT/AIT/AT, SRO layer, tariff floor, specific duties) with rate lineage (BR-DS-04) and property tests (BR-AI-08: rate↑ ⇒ TTI↑).
  - BoE mirror entry + lane + status chain (BR-CUS-02); variance → dispute + guarantee register (BR-CUS-03); challan register + Σ=TTI (BR-CUS-06/08); AIT/AT sub-ledgers (BR-CUS-07, BR-IMP-07); demurrage accrual (BR-CUS-04).
- **Imports**:
  - ImportFile 15-state machine + side states (BR-IF-01); file numbering + C&F custody log (BR-IF-04); per-stage SLA (BR-IF-03); demurrage clocks + 70% alert (BR-IF-02).
  - PI/CI/PL capture + reconciliation grids (BR-DOC-01..08); BL/AWB custody chain (BR-DOC-09..11).
  - Permits registry + gate (BR-PM2-01/02); COO + preferential flag (BR-PM2-03/04).
  - Shipments / milestones / containers (ISO 6346) (BR-SHP-01..05).
  - Insurance CI×110% + claims sub-ledger (BR-INS-01/03/04); C&F master + charge bills (BR-CNF-01/02).
- **TradeFinance**:
  - LC prerequisites (BR-IMP-01/02); term-consistency vs PO/PI (BR-LC-03, BR-PO-07); margin block/release ledger (BR-LC-04, BR-MRG-01); facility exposure (BR-LC-05); presentations + discrepancy workflow + maturity obligations (BR-LC-06); retirement + realized FX (BR-LC-07); charges → cost ledger (BR-LC-08); watchdog sagas T-15/7/3 (BR-LC-09); amendments DoA (BR-LC-10).
  - TT schedule + instruction pack (BR-TT-01..03); SWIFT registry (BR-TT-04); import contracts + IMP matching (BR-TT-05, BR-IMP-08); unified obligation calendar + treasury export (BR-OBL-01..03).

### M4 — Costing + intelligence (weeks 25–30)

- **Costing**: auto-created cost sheet (BR-LCS-01); engine-seeded estimates (BR-LCS-02); lifecycle + completeness gate w/ accruals (BR-LCS-03, BR-IMP-06); allocation engine (drivers/scopes, §23.2, banker's rounding + residual, BR-LCS-06/07); source-doc traceability (BR-LCS-05); finalization via Workflow → revaluation + GL events + variance report (BR-LCS-08); late-bill adjustment versions (BR-LCS-09); cost-vs-recoverable split (BR-LCS-10).
- **Inventory valuation**: batch = file-no provenance (BR-VAL-05); WA provisional→final revaluation + COGS policy (BR-VAL-01/02); append-only value ledger (BR-VAL-03); adjustment maker-checker + period lock (BR-VAL-04).
- **Finance journal engine**: tenant-remappable event→journal map + xlsx export (BR-FIN-01); clearing zero-check blocking file closure (BR-FIN-02, BR-IMP-08); cost centers (BR-FIN-03).
- **Intelligence**: `FeasibilityEngine` (BR-AI-01..03, §23.3 — weights, snapshots, <3s, override path); `CostForecastEngine` v1 (lane medians, deterministic duty, FX drift, low-sample fallback, BR-AI-04/05); `SupplierRiskProvider` (5-pillar decay-weighted, BR-AI-06); OCR gateway (pluggable per D-06, confidence + human-confirm on financial fields, BR-AI-11).
- **Sagas**: ImportFulfilment, LcSettlement watchdogs, CostFinalization (Rebus; swap transport for prod).

### M5 — Reports + hardening (weeks 31–36)

- **Reporting**: inbox-deduped projectors → read models; 5 canned reports (vendor spend, import cost, duty analysis, supplier performance, executive dashboard) + CSV/xlsx export.
- Postgres FTS + pg_trgm omni-search (stretch/optional).
- **RLS hardening phase** (deferred per scope decision): RLS policies in each module migration + `SET LOCAL app.tenant_id` interceptor coordinated with `TransactionBehavior` + 2-tenant CI isolation suite (BR-MT-06).
- Performance (k6 vs NFR gates: duty <300ms, feasibility <3s), security pass, NBR tariff seed snapshot, seed data for the full journey, pilot runbook.

---

## 3. Conventions & verification

- Every new module mirrors `PurchasingModule`: per-module DbContext + schema + design-time factory + migrations + `IUnitOfWork` + `AddMediatorHandlers` + `AddOutbox/AddInbox`; endpoints REPR-style via `MapModulusEndpoints`.
- Money: `numeric(18,4)` + currency; quantities: `numeric(18,6)` + uom; timestamps: `timestamptz` UTC. Approval flows route through the Workflow module only.
- **Traceability**: every P1 rule ID (`BR-*`) maps to ≥1 test with the ID in the test name; pure engines (duty cascade, allocation, feasibility scoring) get unit tests; per-milestone demo = integration test over HTTP via `ModulusWebAppFactory`.
- **Phase-1 exit test** (BRS §2.2): tenant completes vendor → PR → RFQ → PO (feasibility) → PI → insurance → LC (margin blocked) → shipment → BoE (rate-lineaged duty) → duty paid → release → GRN (batch=file-no) → cost sheet finalized → inventory revalued → GL posted → closed with IMP matched — as one end-to-end integration test + the 5 exception journeys.

### Risks (carried from BRS §5.9)

- Workflow engine is critical path — mitigated by test-first development and v1 step-types only.
- Duty-data ops need an owner by M1.
- OCR provider decision (D-06) open until M4.
