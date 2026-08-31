# TradeFlow — Implementation BRS (Core)

**Version:** 1.0 | **Date:** August 2026 | **Status:** Baseline for implementation
**Suite:** [README](README.md) · BRS-Core (this file) · [Business Rules](BRS-Business-Rules.md) · [Phasing & Implementation](BRS-Phasing-Implementation.md)

---

## 1. Document Control

### 1.1 Purpose
Single authoritative BRS for building TradeFlow on the Modulus framework. It merges the v1.0 blueprint with v2.0 Batch 1, resolves conflicts, phases the scope, and maps it to Modulus modules. It does not restate every v1/v2 detail — it references them and governs precedence.

### 1.2 Sources
| ID | Source | Location |
|---|---|---|
| S-v1-00…12 | TradeFlow v1.0 Production Blueprint (13 docs) | `samples/files_1/` |
| S-v2-V00…V04 | TradeFlow v2.0 Batch 1 (5 volumes) | `samples/files_2/` |
| S-MD | Modulus framework (this repo) | repo root, `AGENTS.md` |

### 1.3 Precedence
v2 wins → v1 fills → Modulus alignment overrides on technology (D-01…D-08). All conflicts are logged in Appendix A.

### 1.4 Terminology
Tenant, Company, BU, Site, Department, Position per S-v2-V03 Part B. Import File = the consignment aggregate. AV = Assessable Value; TTI = Total Tax Incidence; DoA = Delegation of Authority; SoD = Segregation of Duties; C&F = Clearing & Forwarding agent; BoE = Bill of Entry; PI/CI/PL = Proforma Invoice / Commercial Invoice / Packing List.

---

## 2. Executive Summary

### 2.1 Vision
TradeFlow is an **AI-native, cloud-native, multi-tenant Source-to-Pay and Import-to-Inventory platform** purpose-built for Bangladesh and South Asia. It makes the **import file — from proforma invoice to landed cost posting — a first-class digital object**, wraps global-grade S2P around it, and embeds AI at every decision point.

**Positioning:** *"The system of record and system of intelligence for everything a South Asian enterprise buys, imports, finances, clears, and costs."*

### 2.2 Problem (quantified)
South Asian importers run four disconnected worlds — procurement in Excel/email, import ops in paper files, trade finance visible only in bank portals, costing reconstructed months later in spreadsheets:

| Pain | Business cost |
|---|---|
| Unknown true landed cost at decision time (duty/freight guessed as flat %; SRO benefits missed) | 3–8% margin leakage; mispriced sales quotes |
| Maverick & duplicate buying | 5–12% addressable spend leakage |
| LC/loan maturity surprises | Penal interest, forced forex at bad rates, facility breaches |
| Demurrage & detention (no container clock ownership) | BDT 2–20 lakh/yr per mid-size importer |
| Assessment disputes lost (no declared-vs-assessed history, no rate lineage) | Overpaid duty never recovered |
| Vendor risk invisible | Stockouts, quality failures, compliance exposure |
| Audit pain (NBR/BB/buyers) | Penalties; failed buyer audits (RMG) |
| Key-person risk (import knowledge in heads) | Operations stall on resignation |

### 2.3 Solution
One platform: register & score vendors → requisitions with budget gates → RFQ with landed-cost-normalized comparison → **pre-PO feasibility score** (predicted landed cost, margin, timeline, supplier risk) → DoA-approved PO → import lifecycle workspace (PI → insurance → LC/TT → shipment → customs → release → GRN) → every cost captured and allocated via the **Landed Cost Engine** → true unit cost into inventory valuation and GL → 3-way match, payment schedule, supplier scorecards, analytics.

### 2.4 Differentiators
1. **BD-native duty & landed cost engine** — CD/RD/SD/VAT/AIT/AT cascade, SRO benefits, tariff values, rate lineage, reproducible calculations.
2. **Import File Command Center** — one aggregate spanning PI→LC→shipment→BoE→GRN→cost sheet.
3. **Trade finance depth** — LC/BTB/UPAS lifecycle, margin as restricted cash, IMP matching, maturity calendar.
4. **Pre-PO Purchase Feasibility Engine** — margin/risk/timeline score gates PO submission.
5. **AI document processing tuned for BD trade paper** — bilingual OCR, BoE/BL/PI extraction, auto-reconciliation.
6. **Priced for BD** — BDT pricing at 5–15% of global-suite TCO, bKash-friendly billing, local support.

### 2.5 Market & segments
- **Primary verticals (BD):** RMG & textile (beachhead), pharma (DGDA permits), food & agro, construction & engineering, chemicals & plastics, electronics assembly, trading houses.
- **Geography sequence:** Bangladesh → Sri Lanka & Nepal → Vietnam & Indonesia (localization) → Pakistan → India (last) → GCC (trading houses).
- **Drivers:** $70B+ import economy, ~8,000 industrial IRC holders, NBR digitization (ASYCUDA World, online Mushak), Bangladesh Bank tightening on IMP/LC/forex discipline, LDC graduation (2026) eroding preferential access.

### 2.6 Plans & pricing (v2 — authoritative)
| Plan | Monthly (BDT) | Users | Import files/yr | Key gates |
|---|---|---|---|---|
| Starter | 30,000 | 15 | 120 | Core P2P + import + LC; canned reports; email support |
| Professional | 75,000 | 50 | 600 | + Sourcing suite, budget control, AI doc processing, feasibility engine, unlimited supplier portal, API access |
| Enterprise | 180,000+ (quoted) | Unlimited | Unlimited | + SSO/SAML, white label, report builder, copilot, sandbox tenant, 99.9% SLA |
| Group/Holding | Custom | — | — | Multi-company consolidation, cross-company analytics |

USD parity (Starter $349 / Pro $849 / Ent $1,999+). Annual prepay −15%. Add-ons: import-file packs, OCR page packs (metered/1,000 pages), SMS/WhatsApp bundles, extra environments, fixed-fee onboarding.

**Subscription mechanics:** Trial(30d) → Active → PastDue → Suspended → Cancelled → Churned; 14-day read-only grace before suspension; data retained ≥ 90 days with export offered. Entitlements resolved as feature flags at login — no hard-coded plan checks in domain code. Usage metering (files opened, OCR pages, SMS) through the standard event pipeline; overages billed monthly in arrears.

### 2.7 Revenue trajectory (planning case)
| Year | Paying tenants | Avg MRR (BDT) | ARR (BDT Cr) |
|---|---|---|---|
| Y1 | 45 | 62,000 | 3.3 |
| Y2 | 140 | 78,000 | 13.1 |
| Y3 | 320 | 92,000 | 35.3 |

Unit economics: gross margin ≥ 72%, CAC payback ≤ 14 months, NRR ≥ 115%.

---

## 3. Business Goals & Traceability

| Goal | Measure (verification) | Primary rule clusters |
|---|---|---|
| G1 Cost truth | 100% of import receipts carry finalized landed cost within 10 days of release | BR-LCS-*, BR-IMP-*, BR-CUS-* |
| G2 Spend control | ≥ 95% of spend via approved PO; contract compliance ≥ 85% | BR-PR-*, BR-PO-*, BR-CNT-* |
| G3 Finance discipline | Zero missed LC/loan maturities; margin cash visible daily | BR-LC-*, BR-TT-*, BR-OBL-* |
| G4 Cycle time | PR→PO ≤ 3 days (indirect); import ETD→release ≤ dwell benchmark −20% | BR-WF-*, BR-SHP-* |
| G5 Compliance | Every BoE reproducible (rate lineage); IMP forms matched ≥ 98% | BR-CUS-*, BR-DS-* |
| G6 Intelligence | Feasibility score on 100% of import POs pre-approval | BR-AI-* |
| G7 Supplier value | Top-80%-spend vendors scored quarterly; risk alerts < 24h | BR-VEN-* |
| G8 Multi-entity governance | Multiple companies per tenant with consolidated + isolated views | BR-MT-*, BR-ORG-* |
| G9 Compliance & audit | Immutable audit trail; document retention ≥ 6 years (NBR); maker-checker on money-adjacent masters | BR-PM-*, BR-IDM-*, BR-NTF-* |

---

## 4. Actors, Organization & Governance

### 4.1 Canonical actors
- **Buy-side:** Requester, Buyer/Procurement Officer, Procurement Head, Category Manager, Sourcing Specialist, Contract Manager, Import Executive, Import Manager, Commercial/Trade-Finance Officer, Finance Manager, CFO, Cost Accountant, AP Clerk, Store/Warehouse Keeper, QC Inspector, Budget Owner, Internal Auditor, Compliance Officer, Tenant Admin, Group Controller.
- **Sell-side/external:** Supplier (local/foreign), C&F Agent, Freight Forwarder, Insurance Provider, Bank (Trade Desk), Surveyor/Inspection Agency.
- **System:** Workflow Engine, Notification Engine, AI Engines, Scheduler, Integration Hub.

### 4.2 Organization hierarchy
```
TENANT (group)
 └─ COMPANY (legal entity: BIN, eTIN, IRC/ERC, base currency, fiscal year)
     └─ BUSINESS UNIT (division/SBU)
         └─ SITE (factory/warehouse/office; port proximity, bonded-license attrs)
             └─ DEPARTMENT (cost-center link)
                 └─ POSITION (approval-routing anchor; users occupy positions)
```
- One user may hold multiple positions across companies; UI carries an active-company switcher; every action records the active company context.
- Documents stamp `company_id + bu_id + site_id + dept_id` at creation (denormalized, validated against the org-tree version).
- Approval routing resolves against the tree **as-of document creation date**; reorgs create new tree versions; in-flight workflows keep their resolution snapshot.
- Cost centers map to department × site; budget ownership follows the tree.

### 4.3 Role templates (18, platform-defined; tenants clone & customize)
Requester, Buyer, Procurement Head, Category Manager, Sourcing Specialist, Contract Manager, Import Executive, Import Manager, Trade Finance Officer, Finance Manager, CFO, Cost Accountant, AP Clerk, Store Keeper, QC Inspector, Auditor (read-all + audit), Compliance Officer, Tenant Admin. Assignment = user × role × scope (org subtree, company list, category, site, amount ceiling, own-vs-all).

### 4.4 Delegation of Authority — default matrix (tenant-configurable)
| Document | Amount (BDT) | Chain (positions) |
|---|---|---|
| PR | ≤ 1L | Dept Head |
| PR | 1L–10L | Dept Head → Procurement Head |
| PR | > 10L | Dept Head → Procurement Head → CFO |
| PO (domestic) | ≤ 5L | Procurement Head |
| PO (domestic) | > 5L | Procurement Head → CFO |
| PO (import) | any | Procurement Head → Feasibility Gate → CFO; > 1 Cr adds MD |
| RFQ award | > 5L | Sourcing Mgr → Procurement Head (+CFO if lowest bid not chosen) |
| LC application | any | Import Mgr → CFO |
| LC amendment | value-increasing | CFO; else Import Mgr |
| Duty rate change | any | Tax Officer (maker) → Finance Head (checker) |
| Landed cost finalization | any | Import Mgr → Finance Head |
| Invoice exception | > tolerance | AP → Finance Head |
| Budget revision | any | Budget Owner → CFO |
| Vendor qualification | strategic category | Vendor Mgr → Procurement Head |

Rules: routing targets **positions**; amount slabs on base-currency equivalent at dated rate; re-approval if amount grows > 5% post-approval; amount-slab change on Return→Resubmit re-resolves the chain from step 1 (anti-slab-dodging); parallel + quorum steps supported (2-of-3 committees).

### 4.5 Segregation of duties & maker-checker
- Creator ≠ approver on approval steps — **enforced by the workflow engine**, not convention.
- Maker-checker pairs (entry role ≠ approve role): vendor bank accounts, duty rates, exchange rates, credit notes, workflow-definition publish, DoA edits.
- SoD conflict matrix per tenant (e.g., `vendor.bank.enter` × `invoice.approve`); overrides need Admin + Compliance dual approval and land on the SoD exception report.
- Time-boxed delegation (vacation) with "X on behalf of Y" stamping; no re-delegation.

### 4.6 Identity & session policies (summary)
- BFF pattern: tokens never reach the browser; HttpOnly session cookie.
- MFA: TOTP baseline; WebAuthn preferred; SMS OTP fallback (flagged phishable); enforce per tenant/role; risk-based step-up (new device, IP-country change, high-value approval above a BDT threshold re-prompts credentials).
- SSO (Enterprise, Phase 2): SAML/OIDC federation per tenant; **roles never auto-mapped from IdP** (explicit admin-approved mapping); SCIM create/suspend only; break-glass local admin with MFA + alert-on-use.
- IP restriction: per-tenant CIDR allowlists (whole-tenant / role-scoped / API-client-scoped) with time-boxed travel exceptions.
- Sessions: Redis registry; sliding idle 30 min (15–120 configurable); absolute 12 h; refresh-token rotation with reuse detection (family revocation); device list with remote revoke; admin kill switch.

---

## 5. Business Lifecycles

### 5.1 Source-to-Contract (S2C)
Demand signal → category strategy → supplier discovery/qualification → RFI (Phase 2) → RFQ/RFP (reverse auction Phase 3) → **landed-cost-normalized comparison** → negotiation → award → contract/framework with rate cards, SLAs, penalties → activation → compliance monitoring → renewal/exit.

Stage rules: sealed bids until deadline; RFP technical/commercial envelope separation; award splits allowed with justification; every award traceable to the frozen comparison snapshot; contract prices become the PO price source with tolerance-gated overrides; off-contract purchases of contracted items raise maverick flags.

### 5.2 Procure-to-Pay / Request-to-Pay (P2P / R2P)
```
PR (catalog / free-text / plan punch-in) → budget check (reserve) → PR approval (DoA)
→ sourcing path (contract call-off | RFQ | direct within threshold)
→ PO (budget commit) → supplier acknowledgement → (expedite)
→ GRN + QC → invoice capture (portal/OCR/manual) → 3-WAY MATCH
→ exception workflow → AP posting instruction → payment status feedback
→ budget consume → closure
```
Default match tolerances (tenant-configurable): price ±2%, qty −0%/+1% (0 for batch-controlled items), tax exact. Edge cases mandated: partial receipts/invoices, over-shipment tolerance, returns/debit notes, price retro-adjustments from contract amendments, closed-period invoice arrival, PO cancellation releasing committed budget, advance payments with adjustment tracking.

### 5.3 Import-to-Inventory (Bangladesh canonical flow)
1. Import plan / **Import PO (IPO)** approved with **feasibility snapshot**.
2. **PI** received → validated vs IPO (price/qty/Incoterm/HS) → PI approval.
3. **Insurance cover note** (CI × 110% default; open-cover utilization check).
4. **LCA form + IRC validation** → **LC application** → margin computed & blocked → LC issued (MT700 logged) → amendments as needed. *(Or TT per Bangladesh Bank advance limits.)*
5. Supplier ships → **shipment** (ETD/ETA, vessel/voyage, containers, milestones); **BL/AWB** captured with custody chain (issued → endorsed → surrendered).
6. Documents presented → bank scrutiny → discrepancy handling → acceptance (usance: maturity created) → **bank endorsement**.
7. C&F nominated → **BoE filed in ASYCUDA** (system mirrors entry) → assessment (lane G/Y/R) → declared-vs-assessed variance workflow → **duty payment** (challan) → examination → release order → gate-out (demurrage clocks close).
8. Inland transport → **GRN** (batch = import file no) → QC → putaway.
9. **Cost sheet** accumulates estimated→committed→actual → finalize → **inventory revaluation + GL postings** → file closed after IMP matching & document archive complete.

**Import File state machine (15 states + side states):**
```
Planned → PO-Linked → PI-Accepted → Finance-Instrumented (LC/TT) → In-Production
→ Shipped → Documents-In-Bank → Documents-Released → At-Port → Under-Assessment
→ Duty-Paid → Released → In-Transit-Inland → Received → Costed → Closed
(+ Held / Disputed / Cancelled)
```
File number format: `IMP-{COMPANY}-{FY}-{SEQ}`. SLA engine per stage with breach alerts; demurrage clock starts at port-defined free days (alert at 70%).

**Critical rules BR-IMP-01…08** — see the rule register. Highlights: no LC without valid IRC + accepted PI; insurance before LC on CFR/FOB terms; BoE lines map to CI lines; duty only against assessed BoE; GRN only after release order; cost sheet cannot finalize with clearing balance ≠ 0; AIT/AT recoverable unless policy elects expensing; file cannot close with unmatched IMP form or missing mandatory documents.

### 5.4 Import-to-Finance
Every import event has a finance shadow: LC margin (restricted cash) → acceptance (liability) → duty challan (tax asset/expense split per component) → GR/IR clearing → landed-cost capitalization → retirement/loan creation (LTR/MPI) → realized FX → IMP reporting. Requirement: **any BDT amount in GL traces to a source document line in ≤ 3 clicks.**

### 5.5 Supplier Collaboration lifecycle
Invitation → self-service registration → qualification (docs, references, financials, site audit for strategic) → approved-with-scope (categories/sites) → transacting (RFQ response, PO ack, ASN, invoice submission, scorecard visibility) → development (corrective actions) → suspension/blacklist with reason taxonomy → offboarding (open-commitment check).

### 5.6 Edge-case coverage charter (applies to every module)
Multi-currency + FX drift between events; partials at every step; cancellations at every state with financial unwind; amendments with version history; back-dated documents vs closed periods; tolerance breaches (accept/reject/escalate); duplicate detection (vendor/invoice/document); force-majeure holds; year-end cutover; BD specifics (SRO expiry mid-shipment, assessment lane escalation Y→R, port-congestion demurrage waivers, BB circular changes with effective dating).

---

## 6. Functional Scope — MVP (Phase 1)

FR numbering: `FR-<ctx>-<nnn>` with ctx ∈ {PLT, ORG, IDM, PRM, WFL, NTF, VEN, SRC, CNT, PRC, GRN, INV, BUD, IMP, SHP, CUS, TF, CST, WMS, FIN, AI, RPT, SRCH, API, MOB, DMS, AUD, SEC, LOC, MDM} per S-v2-V02. Each FR ships with actor, trigger, main/alternate flows, validation set, events, permission, audit expectation (enumerated at sprint level; this section fixes scope).

| # | Module | MVP includes | Deferred (Phase 2/3) |
|---|---|---|---|
| 1 | **Platform Foundation** | Multi-tenancy (RLS backstop), org tree (effective-dated), identity (password + TOTP MFA, session registry), permission framework (capability registry, scopes, SoD), workflow engine v1 (approval-chain, condition, system-action, timer, DoA resolvers, versioning, simulate), notification engine v1 (in-app SSE + email, severity map, templates en/bn), audit trail, document vault (versioned, retention), numbering service, feature flags | Workflow designer UI (JSON editor in MVP), WhatsApp/SMS channels, SCIM, risk-based step-up |
| 2 | **Vendor Management + Supplier Portal** | Vendor master + lifecycle (Prospect→…→Blacklisted), duplicate detection/merge, KYC by type, category qualification + expiry, bank maker-checker, monthly scorecards (auto), portal: registration, doc upload, RFQ respond, PO ack, ASN, invoice submit | Vendor recommendation, full collaboration suite (forecast sharing), portal branding |
| 3 | **Sourcing (RFQ)** | RFQ builder from PR lines, invitations (AVL-enforced), sealed bids + deadline lock, comparison matrix with **landed-cost normalization** (imports), split awards, frozen bid tab, savings baseline | RFI, RFP envelopes + committee scoring, reverse auction |
| 4 | **Procurement Core** | PR (item + free-text, budget gate), PO (domestic/import/service), blanket PO with call-offs, revisions, dispatch/acknowledgement, feasibility gate on import POs, closure rules | Full contract management (rate cards, escalations, milestones), service/CAPEX/project procurement variants |
| 5 | **Budget Control** | Budgets per FY × cost center × category, reserve→commit→consume ledger (append-only), revisions with approval, hard/soft block, utilization views | Multi-year project budgets, forecast-to-spend run-rate |
| 6 | **Goods Receipt & QC** | GRN against PO/shipment (+ASN), gate→tally→QC→putaway stages, over-receipt tolerance, accept/reject/deviation, GR/IR accrual at provisional cost, vendor metric events | Full WMS (bin/rack), tablet tally optimizations |
| 7 | **Invoice & 3-Way Match** | Capture (manual/portal/OCR-assist), line-level 3-way match with tolerances, exception queue + reason codes, duplicate check, import invoice↔CI reconciliation, AP voucher, payment proposal + instruction export (no bank execution) | Email-in OCR pipeline maturity, credit-note automation |
| 8 | **Import Management** | Import File workspace (15 states, milestone tracker, cost widget, doc checklist), import planning (basic), IPO extension fields, PI/CI/PL capture + reconciliation grids, HS code master (8-digit, effective-dated) + item mapping, permits registry + gate, COO capture + preferential flag, shipment + milestones (manual), container tracking (manual/bulk) + demurrage clocks, BL/AWB custody chain, insurance (cover note, CI×110%, claims sub-ledger), Incoterms 2020 matrix, C&F agent master + assignment + charge bills, freight quotes + reconciliation, file cost ledger | Carrier/AIS API ingestion, ASYCUDA direct integration (mirror-entry in MVP), advanced planning board |
| 9 | **Trade Finance** | LC register + full lifecycle (apply→issue→amend→present→accept→retire→close), prerequisites validation (IMP-01/02), margin block/release ledger, facility exposure, discrepancy workflow, maturity obligations, charges→cost ledger, watchdogs T-15/7/3, TT schedule + instruction pack + SWIFT registry (upload-captured), unified payment obligation calendar | BTB LC, import loans (LTR/MPI), guarantees, forex desk, bank statement auto-import |
| 10 | **Customs & Duty** | BoE mirror-entry (status chain, lane), duty cascade computation (CD/RD/SD/VAT/AIT/AT + SRO + tariff-value floor, effective-dated rates, maker-checker, rate lineage), assessment variance → dispute records, challan register + reconciliation, AIT/AT sub-ledgers per FY, clearance checklist, port charges, provisional-release guarantee register | Duty forecasting ML, SRO rule automation, Finance-Act wizard (manual effective-dated entry in MVP) |
| 11 | **Landed Cost** | Cost sheet auto-created per file, estimates seeded from engines, actuals stream in, allocation engine (all drivers, scoping, banker's rounding, residual rule), completeness gate (IMP-06) with accruals, finalization workflow, unit cost per line, variance vs feasibility snapshot | What-if simulator UI, adjustment-version diff views (basic adjustment supported) |
| 12 | **Intelligence v1** | Feasibility engine (heuristic: historical averages + rules, < 3 s, snapshot on PO), cost forecasting v1 (statistical: lane medians, deterministic duty, bank schedule, FX drift), supplier risk v1 (5-pillar decay-weighted), OCR-assisted document capture (PI/CI/PL/BoE/BL) with confidence + source tagging | ML layer (LightGBM sidecar), copilot/RAG, fraud & duplicate detection ML, demand forecasting, recommenders |
| 13 | **Inventory Valuation (handoff)** | Batch/lot creation (batch = import file no), weighted-average valuation, provisional→final revaluation, append-only value ledger, COGS adjustment policy, period-close lock | FIFO layers, serial tracking depth, full inventory module (issues/transfer/counts) |
| 14 | **Reporting & Search (basic)** | 5 core canned reports (vendor spend, import cost, duty analysis, supplier performance, executive dashboard) from marts/read models, export xlsx/CSV/PDF, Postgres full-text + trigram omni-search (Cmd+K) | 15-report catalog completion, report builder, OpenSearch, scheduled distribution, saved views |

**Platform cross-cutting in MVP:** multi-currency (dated, source-tagged rates; customs rate distinct from booking rate), document vault with ≥ 6-year retention, SLA alerts per lifecycle stage, maker-checker on sensitive masters, Bangla/English UI, BD fiscal year (July–June), audit everywhere.

---

## 7. Non-Functional Requirements (v2 targets — authoritative)

| Category | Requirement |
|---|---|
| Performance | P95 read < 200 ms, write < 400 ms @ 500 concurrent users/cell; feasibility < 3 s; duty calc < 300 ms (100 ms desirable); OCR P95 < 45 s/document (async); search-as-you-type < 150 ms; canned reports < 5 s; bulk import 10k PO lines ≤ 5 min |
| Availability | 99.9% monthly (Enterprise SLA); Multi-AZ; RTO 1 h, RPO 5 min; DR warm-standby data + IaC re-creation ≤ 4 h; brown-out mode degrades search/AI before core transactions |
| Scalability | Design point 1,000 tenants/cell, 50M document rows/tenant-year, 2,000 events/s platform-wide; horizontal scaling; partitioned tables; cell-based sharding with edge tenant→cell routing; no cross-cell transactions |
| Security | Zero-trust; OIDC everywhere; RLS as last-line tenant isolation; field-level encryption (bank accounts); KMS per-tenant DEK envelope; OWASP ASVS L2; quarterly pentest; immutable audit (append-only + object lock); maker-checker on money-adjacent masters |
| Compliance | SOC 2 Type I (mo 10) → II (mo 20); NBR retention ≥ 6 years; Bangladesh Bank FX/IMP alignment; data residency in-region where possible else contractual disclosure; GDPR-grade DSR tooling for regional expansion |
| Usability | WCAG 2.1 AA; bilingual en/bn + Bangla numerals option; BDT Lakh/Crore formatting; every computed figure explainable in ≤ 2 clicks; keyboard-complete approvals; PWA for approvals/receiving |
| Auditability | Every state change: who/when/what-diff/why(reason codes)/correlation-id; workflow decisions store input snapshots; AI decisions store model version + feature hash; queryable + exportable per aggregate |
| Data quality | Duplicate-detection precision: vendor ≥ 92%, invoice ≥ 97%; MDM governance for HS/UoM/currencies; all documents carry source (`manual|ocr|api|import`) + confidence |
| Integrability | REST (OpenAPI 3.1) + webhooks (HMAC, retry); bulk NDJSON; rate limits per plan; idempotency keys on all writes |

---

## 8. Out of Scope, Assumptions & Dependencies

### 8.1 Out of scope (v2.0)
Selling-side (sales/CRM/export LC beyond BTB master linkage), production planning/MRP (consumes via API), full accounting GL (posts to it, doesn't replace it), HR/payroll, transport fleet management, direct bank payment execution (instructions generated only).

### 8.2 Assumptions & dependencies
- NBR tariff data maintained as a platform content service (weekly ops pipeline).
- ASYCUDA/BB integrations begin as structured mirror-entry + document upload; APIs adopted when public.
- Banks integrate via SWIFT-copy upload/email ingestion pre-API.
- Tenants provide chart-of-accounts mapping during onboarding.
- Cross-tenant ML pooling only on anonymized/aggregated features with per-tenant opt-in; no tenant-identifiable data in shared models.

---

## 9. Open Decision Log

| ID | Decision | Status | Rationale |
|---|---|---|---|
| D-01 | Runtime = **Modulus framework on .NET 10** (not .NET 9 + MediatR + Dapper as both BRS versions assume) | Resolved | Repo reality; framework provides module system, outbox, sagas, testing harness |
| D-02 | Data access = **EF Core** per-module DbContexts (Dapper-style raw SQL allowed for hot read queries later) | Resolved | Modulus.EntityFrameworkCore + per-module schema ownership; migrations per module |
| D-03 | Identity = **OpenIddict (Modulus.Identity)** replaces Keycloak; JWT carries tenant_id/company_ids/roles claims; external IdPs via Modulus adapters | Resolved | Avoids operating Keycloak; same OIDC semantics; single-realm model preserved |
| D-04 | Eventing = **Modulus transactional outbox + in-process bus** at first; AWS EventBridge/SQS adapter when deployed to AWS | Resolved | Same envelope contract; swappable transport |
| D-05 | Search = **Postgres full-text + pg_trgm** for MVP; OpenSearch in Phase 2 | Resolved | Deferred infra cost; index rebuild path documented |
| D-06 | OCR provider = pluggable gateway (cloud OCR/LLM), selected in M4 | Open | Cost/accuracy benchmark pending |
| D-07 | Postgres **RLS enforced** via per-module migrations + EF Core connection interceptor (`SET LOCAL app.tenant_id`), with 2-tenant CI isolation tests | Resolved | Meets S-v2-V03 defense-in-depth; maps onto Modulus TransactionBehavior |
| D-08 | Billing/payment rails (Stripe/SSLCommerz/bKash) out of MVP platform scope — manual invoicing first | Resolved | SaaS ops module lands Phase 2 |

---

## Appendix A — v1 vs v2 Conflict Resolutions

| Topic | v1 (files_1) | v2 (files_2) | Resolution |
|---|---|---|---|
| MVP duration | 6 months | 8 months | **8 months** |
| MVP scope | Leaner (no portal, no OCR, no QC) | + supplier portal, blanket PO, QC, OCR capture, feasibility v1, PWA | **v2 scope** |
| Pricing | 25k/60k/150k | 30k/75k/180k + Group tier | **v2** |
| USD parity | 349/799/1999 | 349/849/1999 | **v2** |
| Y1 targets | 40 tenants / 3.2 Cr | 45 tenants / 3.3 Cr | **v2** |
| Duty calc latency | 100 ms | 300 ms | **300 ms gate, 100 ms target** |
| Tenants/cell | 500 | 1,000 | **v2 design point** |
| Grace/suspension | unspecified | 14-day read-only grace, 90-day retention | **v2** |
| IMP rules | IMP-01…07 | IMP-01…08 (+IMP-08 IMP matching) | **v2 numbering** |
| Permission model | Role × capability × scope matrix (17 roles) | ~320-capability registry, 18 role templates, ABAC-lite conditions | **v2** (role list extended by one) |
| Workflow engine | JSON state machine (doc 02 §7) | Full step-type registry, DoA re-resolution, simulate mode (V04) | **v2**; v1's DoA matrix retained as default content |
| Stack | .NET 9, MediatR, Dapper, Keycloak | Same | **Overridden by D-01…D-03** |
| UI stack | Next.js 14 | Same (PWA added) | Confirmed; not governed by this BRS |
| Domain modules | Docs 03–08 complete | Not yet written (V05–V16 pending) | **v1 is the working source**, phased per this BRS |
