# ProcureFlow — Phasing & Modulus Implementation Mapping

**Version:** 1.0 | **Date:** August 2026
**Suite:** [README](README.md) · [BRS-Core](BRS-Core.md) · [Business Rules](BRS-Business-Rules.md) · This file

---

## 1. Phasing Overview

| Phase | Window | Theme | Rule-register coverage |
|---|---|---|---|
| P1 — MVP | Months 0–8 | Import-first S2P core: one import file end-to-end with true landed cost | All rules marked P1 |
| P2 — Depth | Months 9–18 | Sourcing depth, BTB/loans, contracts, WMS-lite, report builder, OpenSearch, SaaS ops (billing, SSO/SCIM) | All rules marked P2 |
| P3 — Enterprise/AI | Months 19–36 | ML engines, copilot/RAG, reverse auction, white label, marketplace, ASYCUDA/BB APIs, regional localization | All rules marked P3 |

Design partners (10) onboard at end of P1. Every phase's exit criteria include the NFR gates of BRS-Core §7 measured on staging.

---

## 2. Phase 1 — MVP (Months 0–8)

### 2.1 Milestone plan (6-week cadence, scope-fixed)

| Milestone | Weeks | Delivers |
|---|---|---|
| **M0 Walking skeleton** | 1–2 | `modulus app ProcureFlow` solution; tenancy + identity (OpenIddict password flow, TOTP) + one vertical slice (Vendor CRUD + audit + feature flags) deployed to staging |
| **M1 Platform foundation** | 3–8 | Org tree (effective-dated), capability-based permissions + SoD, workflow engine v1 (approval-chain, condition, system-action, timer, DoA resolvers, versioning, JSON editor + simulate), notification engine v1 (in-app SSE + email, en/bn templates), document vault, numbering, RLS on all module schemas |
| **M2 Procurement core** | 9–16 | Vendor lifecycle + qualification + scorecards, supplier portal (respond/ack/ASN/invoice), PR + budget, RFQ + landed-cost comparison + award, PO (+ blanket, + feasibility gate stub), GRN + QC, invoice 3-way match + AP proposal |
| **M3 Import + trade finance spine** | 17–24 | Import File workspace (15 states), PI/CI/PL + reconciliation, HS master + mapping + duty-rate tables (maker-checker), permits/COO, shipment/milestones/containers + demurrage clocks, BL/AWB custody, insurance, C&F + cost ledger, LC lifecycle + margin + facility + TT + SWIFT registry + obligation calendar, BoE mirror-entry + duty cascade + assessment variance + challans + AIT/AT ledgers |
| **M4 Costing + intelligence v1** | 25–30 | Landed Cost Engine (allocation + completeness + finalization), inventory revaluation + value ledger, GL posting engine (default map + xlsx export), feasibility engine v1 (heuristic + snapshot), cost forecast v1 (statistical), supplier risk v1, OCR-assisted capture (PI/CI/PL/BoE/BL) |
| **M5 Reports + hardening + pilot** | 31–36 | 5 canned reports + executive dashboard, omni-search (Postgres FTS), perf/load (k6) + security pass + RLS isolation suite green in CI, seed NBR tariff snapshot, pilot onboarding runbook, 10 design partners in trial |

### 2.2 Phase-1 exit criteria
1. A tenant completes the full journey: vendor → PR → RFQ → PO (feasibility ≥ threshold) → PI → insurance → LC (margin blocked) → shipment (manual milestones) → BoE (duty computed with rate lineage) → duty paid → release → GRN (batch = file no) → cost sheet finalized → inventory revalued → GL posted → file closed with IMP matched.
2. Zero missed-tenant isolation in the 2-tenant CI RLS suite; SoD violations impossible via workflow engine (BR-PM-03 proven by test).
3. Duty calculation reproducible for any historical consignment (BR-DS-04 proven).
4. NFR gates on staging: feasibility < 3 s, duty calc < 300 ms, P95 read < 200 ms / write < 400 ms at target load.
5. Feasibility override, assessment variance dispute, LC discrepancy, demurrage alert, 3-way-match exception — all five exception journeys demoable end-to-end.

---

## 3. Phase 2 — Depth (Months 9–18)

| Area | Delivers |
|---|---|
| Sourcing | RFI, RFP envelopes + committee scoring + quorum, reverse-auction prep, saved views |
| Contracts | Full contract lifecycle (rate cards, escalations, milestones, renewals, maverick analytics) |
| Trade finance | BTB LC + entitlement engine, import loans (LTR/MPI) + accruals, guarantees, bank statement import, margin analytics |
| Customs | Finance-Act import wizard, SRO rule automation, dispute analytics |
| Inventory/WMS | FIFO layers, serial tracking, bin/rack, counts, full inventory module |
| Reporting | 15-report catalog complete, report/dashboard builder, scheduled distribution, OpenSearch migration (per-tenant filtered aliases) |
| Platform | SaaS ops: billing engine + payment rails (Stripe/SSLCommerz), subscription lifecycle automation, SSO (SAML/OIDC) + SCIM, risk-based MFA step-up, IP allowlists, white label v1, tenant export, WhatsApp/SMS channels |
| Integrations | ERP connectors v1 (Tally XML, xlsx exports formalized), webhook egress |

**Exit:** first Pro-plan conversions; SaaS billing self-serve; OpenSearch omni-search < 150 ms; SOC 2 Type I evidence collection underway.

## 4. Phase 3 — Enterprise/AI (Months 19–36)

ML layer (freight/dwell/assessment-uplift models via sidecar with statistical fallback), delay classifier, vendor/purchase recommenders, copilot with tenant-grounded RAG, fraud & duplicate detection, demand forecasting; reverse auctions; white label GA; fixed assets; offline mobile; ASYCUDA + Bangladesh Bank APIs as they open; multi-company consolidation (Group tier); regional localization packs (Sri Lanka, Nepal); marketplace (banks/insurers/forwarders); SOC 2 Type II; cell sharding at scale.

---

## 5. Modulus Implementation Mapping

### 5.1 Solution shape
One modular monolith generated by `modulus app ProcureFlow` (+ `modulus add-module` per business module), hosted in `ProcureFlow.Api`. Every module follows the Modulus 4-layer layout (Domain / Application / Infrastructure / Presentation) with its **own DbContext, schema, migrations, IUnitOfWork, and handlers** registered via `AddMediatorHandlers` — matching the BRS's bounded-context/schema-per-context design (S-v1 doc 09/10).

### 5.2 Bounded context → Modulus module map

| Bounded context (BRS) | Module | Schema | Owns (aggregates) | Phase |
|---|---|---|---|---|
| Platform (generic) | `ProcureFlow.Platform` | `platform` | OrgNode, WorkflowDefinition/Instance/Task, NotificationTemplate/Rule/Log, DocumentVaultItem, NumberSequence, AuditEntry, FeatureFlag, TenantSettings | P1 |
| Vendor | `ProcureFlow.Vendors` | `vendor` | Vendor, BankAccount (maker-checker), CategoryQualification, Scorecard, EvaluationEvent | P1 |
| Sourcing | `ProcureFlow.Sourcing` | `sourcing` | SourcingCase, Bid, BidScore, Award (frozen snapshot) | P1 |
| Procurement | `ProcureFlow.Procurement` | `proc` | PurchaseRequisition, PurchaseOrder (+ revisions, feasibility snapshot, blanket), Contract (P2) | P1 |
| Budgeting | `ProcureFlow.Budgeting` | `budget` | Budget, BudgetLine, BudgetTxn (append-only) | P1 |
| Import Logistics | `ProcureFlow.Import` | `import` | ImportFile (state machine), ProformaInvoice, CommercialInvoice, PackingList, Shipment, Container, TransportDocument, InsurancePolicy, Permit, CnfAgent, CostElement | P1 |
| Trade Finance | `ProcureFlow.TradeFinance` | `tradefin` | LetterOfCredit (+ amendments/presentations/margin/charges), TtPayment, SwiftMessage, ImportContract, BankFacility, PaymentObligation; (P2: MasterExportLc, ImportLoan) | P1 |
| Customs & Tax | `ProcureFlow.Customs` | `customs` | HsCode, DutyRate (effective-dated), Sro/SroBenefit, BillOfEntry, BoeLine, AssessmentVariance, Challan, BankGuarantee, AdvanceTaxLedger | P1 |
| Costing | `ProcureFlow.Costing` | `costing` | LandedCostSheet, CostElement (staged), CostAllocation, LineLandedCost (+ adjustment versions) | P1 |
| Inventory | `ProcureFlow.Inventory` | `inv` | Grn, QcInspection, Batch, InventoryValueLedger (append-only) | P1 |
| Finance | `ProcureFlow.Finance` | `fin` | ApInvoice/Voucher, PaymentProposal/Instruction, JournalBatch, CostCenterAllocation; (P2: AccrualRun) | P1 |
| Intelligence (supporting) | `ProcureFlow.Intelligence` | `intel` | FeasibilitySnapshot, CostForecast, SupplierRiskScore, OcrExtraction | P1 |
| Reporting | `ProcureFlow.Reporting` | `mart` | Read models/projections + report queries | P1 (5 reports) → P2 full |
| Search | `ProcureFlow.Search` | — | Postgres FTS projections (P1) → OpenSearch indexer (P2) | P1/P2 |
| SaaS Ops | `ProcureFlow.Saas` | `saas` | Subscription, UsageMeter, BillingInvoice, TenantProvisioning state | P2 |

### 5.3 Module dependency graph (`[DependsOn]` wiring)

```
ProcureFlow.Platform  (framework-level: uses Modulus.Platform/Identity/AspNetCore)
 ├─ Vendors ──────────── Platform
 ├─ Budgeting ────────── Platform
 ├─ Sourcing ─────────── Vendors
 ├─ Procurement ──────── Vendors, Budgeting, Sourcing        (feasibility via IFeasibilityEngine abstraction)
 ├─ Inventory ────────── Procurement
 ├─ Customs ──────────── Platform                            (HsCode/DutyRate are platform-scoped data w/ tenant deltas)
 ├─ Import ───────────── Procurement, Customs, TradeFinance?  → NO: Import → Procurement, Customs (TradeFinance references Import by id only)
 ├─ TradeFinance ─────── Import, Vendors                     (file link by id; no reverse dependency)
 ├─ Costing ───────────── Import, Customs, Inventory
 ├─ Finance ───────────── Inventory, Costing, TradeFinance
 ├─ Intelligence ──────── (no hard DependsOn on business modules) — consumes published events; engine abstractions in Shared
 ├─ Reporting ─────────── all business modules (read-only projections)
 └─ Search ────────────── event subscribers only
```

**Circular-dependency seams (contract-first):**
- `IFeasibilityEngine` / `ICostForecastEngine` / `ISupplierRiskProvider` interfaces live in `ProcureFlow.Shared.Application`; implemented by `Intelligence`; consumed by `Procurement`/`Sourcing` — so Procurement does not depend on the Intelligence module.
- Cross-module references (PO ↔ Import File ↔ LC ↔ BoE ↔ Cost Sheet) use **strongly-typed ids only** (`PoId`, `FileId`, `LcId`, `BoeLineId` — shared in `ProcureFlow.Shared.Domain`), never navigation properties across DbContexts.
- Cross-context queries go through in-process `IContextGateway` interfaces (S-v1 doc 09 §8) implemented as mediators queries registered by the owning module.

### 5.4 Framework reuse (what Modulus already provides — do not rebuild)

| BRS need | Modulus capability | Notes |
|---|---|---|
| Modular monolith, ordered DI phases | `IModule` / `ModulusModule`, `[DependsOn]`, `AddModulus<T>` | Host `ProcureFlowHostModule` lists business modules |
| CQRS pipeline (logging → validation → authorization → transaction → idempotency → handler → outbox → audit) | `Modulus.Mediator` behaviours + host `AddMediator()` once; modules `AddMediatorHandlers(...)` | Matches S-v1 doc 09 §5 pipeline 1:1 |
| Per-module DB + migrations | `AddModuleDatabase<TContext>`, `MigrateModulusDatabasesAsync`, design-time factories | Per-module connection strings (`Vendor`, `Proc`, `Import`, …) or one DB multiple schemas (P1 default: **one Postgres database, one schema per module** — set identical connection string, distinct `TablePrefix`/schema) |
| Multi-tenancy (tenant context flows into jobs/consumers) | `ICurrentTenant` (AsyncLocal) + `ModuleDbContext` tenant query filter | Company/org scope implemented as an extension of the same accessor pattern |
| Tenant authorization | `Modulus.Platform.Authorization` permission registry | Extended with the ~320-capability registry + scopes (BR-PM-01) as a ProcureFlow-owned registry |
| Transactional outbox (dual-write safe) | `Modulus.Outbox` + `IIntegrationEventOutbox` auto-enqueue on `SaveChangesAsync` | Event envelope `{eventId, type, version, tenantId, companyId, aggregate, occurredAt, correlationId, causationId, actor, payload}` |
| Inbox dedup for consumers | `Modulus.Inbox` idempotent decorator | At-least-once delivery + exactly-once effect |
| Long-running orchestration (ImportFulfilment, LcSettlement, CostFinalization, VendorOnboarding, TenantProvisioning sagas) | `Modulus.Sagas` (Rebus) | Timers for watchdogs (PI 14 d, latest-shipment T-7, demurrage 70%) |
| Identity (OIDC, password grant, external IdPs) | `Modulus.Identity` (OpenIddict) — decision D-03 | Claims: tenant_id, company_ids, roles, plan; MFA via TOTP; external SSO via adapters (P2) |
| HTTP idempotency keys on writes | `Modulus.AspNetCore` idempotency middleware | NFR "idempotency keys on all writes" |
| API versioning, rate limiting, CORS, security headers, health probes | `Modulus.AspNetCore` helpers | Wired by `modulus app` template |
| OpenAPI with JWT bearer scheme | `AddModulusOpenApi` | OpenAPI 3.1 output |
| Feature flags (plan entitlements) | `Modulus.AspNetCore` FeatureFlags (`IFeatureManager`, `RequireFeature`) | Entitlement resolution at login → flag map; domain layer never reads flags (V00 §10) |
| Correlation id end-to-end | `ICorrelationContext` + middleware + outbound propagation | Flows into outbox events + audit rows (NFR auditability) |
| PII/field encryption (vendor bank accounts) | `[ProtectedPersonalData]` + DataProtection-backed protector + search-hash columns | Meets "field-level encryption for bank accounts" |
| Background jobs (scorecards monthly, SLA scans, watchdogs, metering) | `Modulus.Platform.BackgroundJobs` | Tenant-fan-out per BR-MT-02 |
| Caching (permission sets, tenant context, tariffs) | `Modulus.Platform.Caching` (in-memory; Redis package for scale-out P2) | |
| File storage (document vault) | `Modulus.Platform.Storage` local (P1) / `Modulus.Storage.S3` (AWS deploy) | Keys prefixed `tenant/{tenant_id}/…` |
| Realtime (notification bell SSE) | `Modulus.Platform.SignalR` (in-process) | Redis backplane package at scale |
| Observability (traces, metrics, correlation) | `Modulus.Observability` | OTel export to Grafana/CloudWatch |
| Integration-test harness | `Modulus.Testing` per-context SQLite factory + authenticated clients | Plus Postgres Testcontainers suite for RLS (below) |

### 5.5 Gaps to build on top of Modulus (ProcureFlow-owned backlog)

| # | Gap | Phase | Notes |
|---|---|---|---|
| G1 | Workflow engine (definitions, instances, tasks, step-type registry, DoA resolvers, CEL-style sandboxed expressions, simulate mode) | P1 | Largest P1 build; pure domain service + hosted SLA scanner; JSON definitions versioned in DB |
| G2 | Notification engine (rules, severity map, templates en/bn, channel adapters: in-app/SignalR, email SMTP/SES; SMS/WhatsApp P2) | P1 | Subscribes to integration events via Inbox |
| G3 | Duty calculator + rate-lineage resolution (BR-AI-07/08) | P1 | Pure, property-tested domain service in `Customs` |
| G4 | Landed Cost allocation engine (BR-LCS-06/07) | P1 | Pure domain service in `Costing` |
| G5 | Feasibility / forecast / risk engines v1 | P1 | `Intelligence` module; statistical layer only |
| G6 | Org tree (ltree or closure table, effective-dating) + position occupancy | P1 | `Platform` module |
| G7 | Capability registry + scope predicates + SoD matrix + delegation | P1 | Extends Modulus authorization |
| G8 | Document vault (versioning, retention, OCR-extracted fields, AV-scan hook) | P1 | Over `Modulus.Storage` |
| G9 | Numbering service (per-tenant sequences, gapless option for legal docs, FY-aware formats) | P1 | Concurrency-safe (`SELECT … FOR UPDATE` on sequence rows) |
| G10 | Postgres RLS integration: per-module migrations (`ENABLE ROW LEVEL SECURITY` + policy template) + EF Core connection interceptor issuing `SET LOCAL app.tenant_id` inside the ambient transaction | P1 | Coordinates with `TransactionBehavior`; app role NOBYPASSRLS; migrations role separate |
| G11 | FX rate service (dated, source-ranked, NBR customs rate vs bank booking rate) | P1 | `Platform` or `Finance` |
| G12 | OCR gateway (pluggable provider) + reconciliation suggestion service | P1 (M4) | Decision D-06 |
| G13 | Sagas: ImportFulfilment, LcSettlement, CostFinalization, VendorOnboarding | P1 | On `Modulus.Sagas` |
| G14 | Read models/projections + 5 canned reports + executive dashboard APIs | P1 | `Reporting` module; projectors consume events via Inbox |
| G15 | Postgres FTS + pg_trgm omni-search projections | P1 | Swap to OpenSearch indexer P2 |
| G16 | Event → journal posting engine + tenant-remappable COA map + clearing-account checker | P1 | `Finance` |
| G17 | Supplier portal surface (separate auth scope, restricted views) | P1 | Same host, policy-isolated controllers; supplier identity via OpenIddict with vendor claim |
| G18 | SaaS ops (subscription, metering, billing, provisioning saga, white label) | P2 | Decision D-08 |
| G19 | Tenant→cell routing + cell tooling | P2/P3 | Single cell until scale demands |

### 5.6 Data & multi-tenancy approach (decision D-02 / D-07)

- **P1 topology:** one PostgreSQL 16 database; one schema per module (`platform`, `vendor`, `sourcing`, `proc`, `budget`, `import`, `tradefin`, `customs`, `costing`, `inv`, `fin`, `intel`, `mart`); each module's DbContext maps its entities to its schema via `modelBuilder.HasDefaultSchema(...)` in its own migrations.
- Every tenant table: `tenant_id uuid NOT NULL`, company scoping where applicable, standard audit columns, `row_version` optimistic concurrency.
- Money `numeric(18,4)` + `currency char(3)`; quantities `numeric(18,6)` + uom; `timestamptz` (UTC) everywhere; business dates `date`.
- **RLS defense-in-depth** (BR-MT-06): module migrations enable RLS + policy `tenant_id = current_setting('app.tenant_id')::uuid`; a scoped `DbConnectionInterceptor`/connection-opener sets `SET LOCAL app.tenant_id` from `ICurrentTenant` whenever Modulus's `TransactionBehavior` opens a transaction. Soft-delete only on masters (`archived_at`); transactional docs use status.
- Partitioning (hash by tenant / monthly range on append-only: outbox, audit, value ledger, notifications) deferred to P2/P3 at scale — design tables so partition keys are forward-compatible (`tenant_id` in PKs; time columns in PKs of append-only tables).
- **Testing:** `Modulus.Testing` SQLite harness for module integration tests; a dedicated **Postgres Testcontainers suite** runs the 2-tenant RLS isolation matrix (create tenants A/B, run every repository method under A, assert zero B rows) + duty-engine property tests — both mandatory in CI.

### 5.7 Eventing & sagas approach (decision D-04)

- Domain events implementing `IIntegrationEvent` flow through the Modulus transactional outbox in the same transaction as state changes (satisfies the dual-write requirement).
- P1 dispatcher: in-process/`Modulus.Events` bus + Inbox-deduped handlers (projections, notifications, search indexer, metering). The envelope matches S-v1 doc 09 §4 so an **EventBridge/SQS adapter can replace the transport in P2** without touching publishers/consumers.
- Saga timers (PI nudge 14 d, LC latest-shipment T-7, demurrage 70%, maturity T-7/3/1) run on `Modulus.Sagas` deferred messages; all watchdog rules of BR-LC-09/BR-IF-02 implemented as saga timeout handlers.

### 5.8 Build & verification conventions

- Follow repo `AGENTS.md`: build with 0 warnings, unit tests + RLS suite green, `dotnet format` clean before merge.
- Every P1 business rule in the register maps to ≥ 1 automated test (traceability per S-v2-V02 §6); rule IDs cited in test names.
- Walking-slice-first: each milestone demos over HTTP via `Modulus.Testing`-style integration tests before layering breadth.
- Generated-app hardening suite (rate limiting, security headers, idempotency, health probes, OpenAPI bearer) stays enabled from M0.

### 5.9 Risks & mitigations

| Risk | Mitigation |
|---|---|
| Workflow engine (G1) is the schedule's critical path | Build v1 step-types only (approval-chain, condition, system-action, timer) in M1; JSON editor not designer; simulate mode from day one; hard BR-WF-01/02 semantics test-first |
| Duty/tariff data operations (NBR content) underestimated | Assign a data-ops owner from M1; seed snapshot by M5; Finance-Act wizard is P2 (manual effective-dated entry in P1) |
| Multi-schema + RLS + per-module DbContext interaction bugs | Dedicated Postgres Testcontainers CI suite from M0; keep SQLite tests for logic, Postgres tests for isolation |
| Feasibility engine credibility at launch | v1 heuristic with transparent factor table + immutable snapshots; calibration report from pilot data (BR-AI-03) |
| OCR accuracy on BD trade paper | Pluggable gateway (D-06); every extracted financial field requires human confirm (BR-AI-11); fallback manual entry |
| Scope creep from design partners | Phase gates + feature flags; every new request lands P2 unless it blocks the core journey |
| Key-person import-domain knowledge | The rule register + state machines encode it; onboarding doc generated from this suite |

---

## 6. Immediate Next Steps

1. Ratify this BRS suite (stakeholder sign-off on decision log D-01…D-08 and phasing).
2. M0 kick-off: generate the Modulus solution skeleton and the vertical slice.
3. Stand up the NBR tariff content pipeline owner + data contract (weekly ops feed).
4. Recruit/confirm the domain consultant (customs/C&F) for M3–M4 rule verification.
5. Draft the traceability matrix skeleton (`rule ID → tests`) in CI from M1.
