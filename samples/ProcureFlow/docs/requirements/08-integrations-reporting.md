# 08 — Inventory & Finance Integration + Reporting Modules

# A. Inventory Integration

## 8.1 Goods Receiving & Warehouse Receiving
**Objectives:** accurate receipt against PO/shipment with QC gating; the quantity source-of-truth for landed cost division and vendor metrics.
**Rules:** GR-01 GRN references PO (+ shipment/CI for imports); over-receipt within tolerance else approval; GR-02 stages: Gate Entry → Unloading tally → QC sampling/inspection → Accept/Reject/Deviation-accept → Putaway (bin); GR-03 shortage/damage on imports recorded with survey evidence → insurance-claim draft (doc 04 §4.15) and cost-treatment per LCS-04; GR-04 GRN posts GR/IR accrual (provisional cost = PO price or forecast landed; revalued at sheet finalization); GR-05 vendor metrics events emitted (OTD vs. need-by, acceptance rate).
**Data:** `grn` (po, shipment?, site, status, gate_pass, vehicle) · `grn_line` (po_line, ci_line?, received_qty, accepted_qty, rejected_qty, deviation_reason, bin) · `qc_inspection` (plan, samples, results jsonb, decision).
**UI:** receiving cockpit (expected-today list from ASN/ETA), tally screen (tablet-friendly), QC worksheet, discrepancy report with photo upload.
**Workflow/Approval:** QC decision (QC Inspector; deviation-accept → Store Mgr + QC Head); over-receipt → Buyer.
**Reports:** receipts register, rejection Pareto, dock-to-stock time. **Notifications:** arrival due, QC pending > SLA, rejection (to buyer & vendor).

## 8.2 Batch / Lot / Serial Tracking
Item master flags tracking mode (none/batch/serial). Imports: default batch = import file no (full provenance: batch → file → BoE → supplier — recall-ready in one query, critical for pharma/food). `batch` (item, batch_no, mfg/exp dates, origin_file, attributes jsonb) · `serial_unit` (item, serial, batch?, status, location). Expiry-aware: FEFO suggestion at issue (when issuing is in scope via ERP integration, we expose batch APIs). Reports: traceability (forward/backward), expiry horizon.

## 8.3 Inventory Valuation & Cost Adjustment
- Methods per tenant/item-class: **weighted average (default)** or FIFO layers. Receipt at provisional cost (GR-04); on `LandedCostFinalized` → revaluation: if stock still on hand, adjust unit cost/layers; consumed portion → expense/COGS adjustment line (policy: adjust COGS vs. variance account).
- `inventory_value_ledger` append-only: (item, site, txn_type receipt/revalue/adjust, qty, unit_cost, value_delta, source_doc) — auditable valuation trail.
- Manual cost adjustments: maker-checker + reason taxonomy; period-close lock (no postings into closed period; late adjustments post to open period flagged "prior-period").
**Reports:** stock valuation by item/site, revaluation impact log, provisional-vs-final cost variance.

# B. Finance Integration

## 8.4 Accounts Payable
Vendor sub-ledger fed by matched invoices (doc 03 §3.6) and charge bills (freight/C&F/port/insurance): aging, due-date engine (terms from PO/vendor), payment proposal runs (filter: due window, vendor, currency) → payment instruction file export (bank format xlsx/NACHA-like local formats) → settlement entry (ref, date) → vendor advice email. Debit notes from rejections offset. **No direct bank execution in v1** — instruction generation + manual confirmation keeps scope and risk contained.

## 8.5 General Ledger Integration
- Internal **posting engine** → journal lines against tenant-mapped COA; export adapters: API push (ERPNext/Odoo/SAP B1 connector), file export (Tally XML, xlsx), or webhook.
- Event → journal map (defaults, tenant-remappable):
| Event | Dr | Cr |
|---|---|---|
| GRN (provisional) | Inventory/GIT | GR/IR Accrual |
| Invoice matched | GR/IR | AP–Vendor |
| Duty challan paid | Duty Clearing (per component; AIT/AT → Advance Tax Asset) | Bank/Cash |
| LC margin blocked | Margin with Bank (restricted) | Bank |
| Bank charges | Import Charges Clearing | Bank |
| Landed cost finalized | Inventory (delta to true cost) | respective Clearing accounts |
| VAT recoverable | Input VAT Asset | VAT Clearing |
| Loan created (retirement) | AP/Bank | Import Loan Liability |
| FX difference | FX Gain/Loss | counter |
- Clearing-account zero-balance check per file at closure (any residue blocks closure → reconciliation task).

## 8.6 Cost Center & Department Allocation
Every PR/PO line carries cost center (department×site default, project optional); landed cost postings inherit; service/overhead charge bills allocatable across cost centers by % matrix. Reports: spend & landed cost by cost center, project import cost rollup.

## 8.7 Budget Consumption & 8.8 Accrual Posting
Budget ledger transitions per doc 03 §3.8 (consume on GRN or invoice — tenant policy). Period-end accrual automation: GRN-not-invoiced list → accrual journal (reversing); expected-cost accruals for files in transit (optional policy) for monthly true landed visibility.

# C. Reporting Modules (15)

**Platform:** all reports run on the **reporting replica + marts** (doc 10 §5); each supports filter set (period, company/BU/site, vendor, category, item, HS, port, currency view), drill-through to source documents, export (xlsx/CSV/PDF), scheduling (email/S3 drop), and saved views. RLS enforced identically on replica.

| # | Report | Key content & special features |
|---|---|---|
| 1 | Vendor Spend Analysis | Spend cube (vendor×category×period), Pareto, share-shift vs. prior period, contract vs. maverick split |
| 2 | Import Cost Analysis | Per-file/per-item element breakdown, FOB→landed waterfall, lane comparison |
| 3 | Duty Analysis | Component mix by HS/category, effective duty % trend, SRO savings realized, computed-vs-assessed uplift map |
| 4 | VAT Analysis | Import VAT paid vs. recoverable, AT ledger movement, Mushak-support export |
| 5 | Supplier Performance | Scorecard league, OTD/quality trends, risk-grade migration matrix |
| 6 | Purchase Trend Analysis | Price indices per item/category, qty seasonality, PPV waterfall |
| 7 | Landed Cost Trend | Unit landed cost time series per item (FX-normalized toggle), element contribution stack |
| 8 | Cost Variance Analysis | Forecast vs. actual per element, variance distribution, top offender files, engine-calibration view |
| 9 | Product Profitability | Selling price vs. landed cost margin per item/period; what-if duty scenario overlay |
| 10 | Country-wise Import Analysis | Value/volume by origin, concentration risk, preferential-COO utilization |
| 11 | HS Code-wise Analysis | Value, duty incidence, assessment disputes per HS; Finance-Act delta impact |
| 12 | Container Cost Analysis | Per-container landed logistics cost, demurrage/detention incidence, dwell distribution |
| 13 | Port Clearance Analysis | Stage cycle times by port/C&F, lane (G/Y/R) distribution, query Pareto |
| 14 | Customs Duty Forecast | Open-PO + plan portfolio duty projection by month, scenario (draft Finance Act) comparison |
| 15 | Executive Dashboard | KPI wall: open commitment, in-transit value, LC outstanding & maturity ladder, margin blocked, avg clearance days, landed-cost variance %, feasibility-score vs. outcome, alerts feed |

**Dashboard tech:** Next.js + Recharts on mart APIs; widgets configurable per role; mobile-responsive read mode; "snapshot to PDF" board pack for monthly management meetings.

**Notification digests:** weekly procurement digest (savings, exceptions), treasury digest (doc 05 §5.9), monthly duty & variance digest to CFO/Tax Officer.
