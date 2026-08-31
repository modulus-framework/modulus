# 12 — UI/UX Screen Catalog & User Journeys

Frontend stack: **Next.js 14 (App Router) · TypeScript · Tailwind CSS · shadcn/ui · TanStack Query/Table · React Hook Form + Zod · Recharts**. Authentication via Keycloak (OIDC) through a BFF route handler; the browser never holds raw tokens.

---

## 1. Next.js Application Structure

```
apps/web/
├── app/
│   ├── (auth)/login/  callback/                # Keycloak OIDC redirect handling (BFF)
│   ├── (shell)/                                # Authenticated layout: sidebar + topbar + omni-search
│   │   ├── dashboard/                          # Role-aware landing dashboard
│   │   ├── inbox/                              # Workflow task inbox (approvals, exceptions)
│   │   ├── vendors/        [vendorId]/
│   │   ├── sourcing/       requisitions/ rfq/ rfp/ comparisons/
│   │   ├── purchase-orders/ [poId]/ feasibility/
│   │   ├── contracts/      invoices/  grn/
│   │   ├── imports/        files/[fileNo]/  shipments/ containers/ documents/
│   │   ├── trade-finance/  lcs/[lcId]/ btb/ loans/ payments/ swift/
│   │   ├── customs/        boe/[boeId]/ duty-structures/ hs-codes/ disputes/
│   │   ├── costing/        cost-sheets/[sheetId]/ landed-cost/ history/
│   │   ├── budgets/        spend/
│   │   ├── reports/        [reportKey]/        # 15 report modules, server components
│   │   ├── admin/          org/ users/ roles/ workflows/ doa/ settings/
│   │   └── settings/       profile/ notifications/
│   ├── api/                                    # BFF route handlers (token exchange, proxy)
│   └── layout.tsx  globals.css
├── components/   ui/ (shadcn)  domain/ (POCard, DutyBreakdown, FeasibilityGauge, LcTimeline…)
├── lib/          api-client.ts  auth.ts  rbac.ts  format-bdt.ts
└── features/     one folder per bounded context, mirroring backend contexts
```

**Shell conventions**
- Left sidebar groups: Procure · Import · Finance · Customs & Costing · Reports · Admin — filtered by the user's permission set (RBAC claims resolved server-side, components render via `<Can capability="po.approve">`).
- Topbar: tenant/company switcher, omni-search (OpenSearch-backed, `Cmd+K`), notification bell (SSE), task-inbox badge.
- All money rendered through `formatBDT()` (Lakh/Crore grouping toggle per user preference); dual display `BDT + FCY` wherever an FX pair exists.
- Every document screen follows the same anatomy: **header strip (doc no, status chip, owner, amounts) → tab set (Details / Lines / Documents / Costs / Workflow / Audit) → right rail (timeline + linked docs graph)**.

---

## 2. Screen Inventory by Module

Notation: **L** list, **D** detail, **F** form/wizard, **M** modal, **B** board/kanban, **R** report canvas.

### 2.1 Core Procurement

| Module | Screens |
|---|---|
| Vendor Management | L Vendor directory (risk chip, scorecard sparkline) · D Vendor 360 (tabs: Profile, Qualification, Banks 🔒maker-checker, Documents, Performance, Risk, Transactions) · F Registration wizard (4 steps: identity → compliance docs → banks → categories) · M Blacklist/suspend with reason · B Qualification pipeline |
| Supplier self-service portal | Separate `(portal)` route group: registration, doc upload w/ expiry, RFQ response, PO acknowledgement, ASN, invoice status |
| Purchase Requisition | L My PRs / Dept PRs · F PR create (catalog or free-text lines, budget check inline) · D PR with approval trail |
| RFQ / RFP | F RFQ builder (lines, invited vendors, sealed-bid toggle, deadline) · D RFQ cockpit (response tracker) · RFP adds technical/commercial envelope tabs |
| Quotation Comparison / Bid Analysis | R Comparison matrix — vendors × lines, **landed-cost-normalized** column toggle, award split editor, sensitivity slider (FX, duty) |
| Purchase Order | L PO register · F PO create (from PR/award; **Feasibility panel docks right and live-updates as lines change**) · D PO 360 (tabs incl. Feasibility snapshot, Amendments, GRN/Invoice match status) |
| Contract Management | L Contract repository (expiry heat) · D Contract (clauses, rate cards, utilization meter) · M Renewal |
| Invoice Management | L Invoice workbench (match-status lanes: Matched / Price Var / Qty Var / Missing GRN) · D 3-way match screen — side-by-side PO·GRN·Invoice with variance highlighting · M Tolerance override (requires reason + role) |
| Goods Receipt | F GRN entry (against PO/shipment, batch & QC fields) · D GRN with putaway status |
| Budget Control | D Budget tree (reserve/commit/consume bars per cost center) · M Budget transfer |
| Spend Analysis | R Cube explorer: category × vendor × period, pareto, maverick-spend flag |

### 2.2 Import Management

| Module | Screens |
|---|---|
| Import File | **D Import File Command Center** — the flagship screen: horizontal milestone tracker (PI → LC → Shipment → Port → BoE → Release → GRN → Costing), cost accumulation widget (estimated vs committed vs actual stacked bars), document checklist with completeness %, exception banner |
| Import Planning | B Planning board by quarter · F Plan → auto-spawn import POs |
| PI / CI / Packing List | F capture forms with line-level reconciliation grid (PI vs CI vs PL deltas auto-flagged) |
| HS Code Management | L Tariff browser (8-digit tree, duty cascade preview per code) · D HS code with rate lineage timeline |
| Permits & COO | L Permit register (expiry countdown) · F COO capture with SAFTA/APTA preferential-rate eligibility check |
| Shipment & Schedule | L Shipment board (milestone columns) · D Shipment (vessel/voyage, ETD/ETA drift chart) |
| Container Tracking | L Container grid (demurrage clock chips: green/amber/red) · D Container timeline |
| BL / AWB | F Document capture with custody-chain stepper (issued → endorsed → surrendered) |
| Insurance | F Cover note (auto CI×110% suggestion) · L Open-cover utilization |
| Customs & Port Clearance | D BoE workspace — ASYCUDA-mirror line grid with per-line duty cascade, assessment variance panel (declared vs assessed, dispute launcher), lane chip (Green/Yellow/Red) · F Duty payment instruction |
| C&F Agent | L Agent directory (AIN, scorecard) · D Rate card editor · R Agent performance |
| Import Cost Tracking | D Cost ledger per file: cost_type × stage matrix, drill to source doc |

### 2.3 Trade Finance

| Module | Screens |
|---|---|
| LC Management | F LC application wizard (PI link, margin calc, facility headroom check) · **D LC lifecycle screen**: vertical timeline (applied → issued → amended → docs presented → discrepancy → acceptance → maturity → retired), margin widget, exposure meter against bank facility |
| Back-to-Back LC | D BTB linkage view — master export LC ↔ BTB entitlement gauge (≤75%), maturity alignment warnings |
| Bank Contracts / IMP | L IMP matching workbench (unmatched BoE↔IMP highlighted) |
| TT & SWIFT | F TT request · L SWIFT registry (MT103/700 chain viewer) |
| Import Loans | F LTR/MPI creation from maturity bill · L Loan register w/ interest accrual |
| Payment Schedule | R Unified obligation calendar (LC maturities, loan installments, TT dates) with 30/14/7-day alert bands |

### 2.4 Customs, Tax & Landed Cost

| Module | Screens |
|---|---|
| Duty Structure Mgmt | D Duty structure per HS+SRO combo, effective-dated rate editor with overlap guard, **"explain calculation" drawer** showing formula cascade with live numbers |
| Tax Payment Tracking | L Payment register by type (CD/RD/SD/VAT/AIT/AT), treasury challan refs |
| Cost Sheets | **D Cost Sheet workspace**: element grid (amount, allocation driver, status), allocation preview per product line, waterfall chart CIF → landed; Finalize button gated by clearing-account zero check · M Post-finalization adjustment |
| Historical Cost | R Landed cost trend per SKU/HS (P50 band), variance decomposition (FX / duty / freight / other) |

### 2.5 Intelligence Surfaces

| Surface | Where it appears |
|---|---|
| Feasibility Gauge | PO create (live), PO detail (frozen snapshot), approval task card |
| Counterfactual hints | Feasibility panel — "switching to sea freight raises score +8" |
| Supplier risk chip | Vendor list/detail, RFQ invite picker, comparison matrix |
| Duty forecast | HS code detail, import planning, cost sheet (expected vs actual) |
| Delay prediction | Shipment board ETA badges |
| Recommendations | PR line "suggested vendors", reorder hints on dashboard |

### 2.6 Admin

Org tree editor (drag-drop company→BU→site→dept) · User & role assignment with permission-matrix preview · DoA slab editor · Workflow designer (versioned JSON, visual step graph, simulate mode) · Tenant settings (FX source, tolerance %, fiscal calendar, number formats) · Audit explorer.

---

## 3. Key User Journeys

### J1 — Procurement Officer: PR → Award → PO (happy path)
1. Inbox shows approved PR assigned to buyer → opens PR, clicks **Create RFQ** (lines prefilled).
2. Picks vendors (risk chips visible; blacklisted vendors unpickable), sets sealed-bid + deadline → vendors respond via portal.
3. Deadline passes → Comparison matrix; toggles **landed-cost basis**; system normalizes FOB/CIF offers with duty + freight estimates.
4. Awards split 70/30 → **Create PO** → Feasibility panel computes (<3 s): score 74 🟢, margin 18.4%, ETA 42 days.
5. Submits → DoA routes to Procurement Head (amount slab) → approved → PO dispatched to vendor portal; budget moves reserve→commit.

### J2 — Import Executive: PI → LC → Clearance → GRN
1. Creates Import File from approved import PO; uploads PI → reconciliation grid green.
2. Opens insurance cover note (auto CI×110%) → LC application wizard: margin 20% computed, facility headroom OK → bank issues, SWIFT MT700 logged.
3. Shipment milestones update ETD/ETA; container demurrage clock starts at port arrival.
4. C&F agent files BoE; Import Executive mirrors ASYCUDA entry — per-line cascade auto-computed; assessed value +6% over declared → variance panel → accepts assessment (within tolerance) → duty payment instruction → challan captured.
5. Release order → GRN created against shipment with batch = import file no → cost sheet flips to Accumulating.
6. All actuals in → cost sheet Ready → Finance finalizes → inventory revalued, `LandedCostFinalized` event posts GL.

### J3 — CFO: feasibility override
PO score 52 🔴 (margin below floor due to RD hike). Approval task shows score breakdown + counterfactuals. CFO opens duty forecast → confirms SRO exemption pending → overrides with mandatory justification → audit log records snapshot + reason → PO proceeds flagged "override".

### J4 — Finance Manager: LC maturity & retirement
Payment calendar shows usance bill maturing in 7 days (amber). Opens LC screen → creates LTR loan to retire → FX rate captured at retirement → margin released → obligation calendar updates → GL postings preview before commit.

### J5 — Tenant Admin: onboarding a new business unit
Org tree → add BU + sites/departments → clone DoA slabs from existing BU → adjust LC slab → assign users to positions → workflow simulate mode verifies a 75 L PO routes Head→CFO → publish workflow v2 (in-flight instances stay on v1).

---

## 4. Notification Matrix (consolidated)

Channels: **In-app (bell + inbox)** always · **Email** for approvals/escalations/external · **SMS (BD gateways)** critical only · Digest option per user.

| Event | Recipients | Channel | Urgency |
|---|---|---|---|
| Approval task assigned / escalated | Approver, then supervisor | In-app + email | Normal / High |
| Feasibility below threshold on submit | Requester, CFO (if override path) | In-app | High |
| RFQ deadline T-24h, vendor non-response | Buyer; vendor (portal+email) | Email | Normal |
| LC discrepancy raised by bank | Trade finance team, import exec | In-app + email + SMS | Critical |
| LC maturity / loan installment T-30/14/7/1 | Finance manager, CFO at T-7 | In-app + email | Escalating |
| Container demurrage clock ≥ amber | Import exec, C&F agent | In-app + SMS | High |
| Assessment variance > tolerance | Import exec, customs lead | In-app | High |
| Permit / vendor document expiry T-30/7 | Owner, compliance officer | Email digest | Normal |
| Cost sheet Ready for finalization | Finance manager | In-app | Normal |
| Budget consumption ≥ 80% / 95% | Budget owner, finance | In-app + email | Normal / High |
| Price variance hold on invoice | AP clerk, buyer | In-app | Normal |
| Workflow SLA breach | Process owner, admin | In-app + email | High |

Delivery: domain events → EventBridge → notification service (SQS) → channel adapters; per-tenant templates (Bangla/English), quiet hours, and per-user channel preferences honored. SSE pushes the in-app bell.

---

## 5. UX Principles & Non-Functional UI Requirements

- **Status-first design**: every document renders its state machine as a chip + milestone tracker; users never guess "where is it".
- **Explainability everywhere**: any computed number (duty, score, allocation) opens a drawer showing the formula with live inputs and rate lineage — mirrors DS-04 reproducibility.
- **Exception-driven inbox**: the task inbox is the home screen for operational roles; lists are secondary.
- **Bilingual**: en/bn locale files; Bangla numerals optional; BDT Lakh/Crore formatting default for BD tenants.
- **Performance**: server components + streaming for reports; TanStack virtualized tables for >1k rows; P95 route transition < 300 ms.
- **Accessibility**: WCAG 2.1 AA, full keyboard support on approval actions, color-blind-safe status palette (shape + color).
- **Responsive**: desktop-first; approval/inbox/dashboards fully usable on mobile web for on-the-go approvers.
- **Audit affordance**: every screen's Audit tab streams the immutable audit_log filtered to that aggregate.
