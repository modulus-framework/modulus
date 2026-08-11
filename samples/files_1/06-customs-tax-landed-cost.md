# 06 — Customs & Tax Modules + Landed Cost Management

## 6.1 Bangladesh Duty Structure — the Computation Core

### Assessable Value (AV)
```
CIF = FOB + Freight + Insurance        (per BoE line, in foreign currency)
AV  = CIF × customs_exchange_rate × (1 + landing_charge_pct)   -- landing charge default 1%
```
Customs exchange rate = NBR-notified monthly rate (rate table, source-tagged), distinct from bank booking rate — both stored.

### Duty Cascade (order matters; per HS line, dated rates)
```
CD  = AV × cd_rate                                  -- Customs Duty
RD  = AV × rd_rate                                  -- Regulatory Duty
SD  = (AV + CD + RD) × sd_rate                      -- Supplementary Duty (on duty-paid value)
VAT = (AV + CD + RD + SD) × vat_rate                -- usually 15%
AIT = AV × ait_rate                                 -- Advance Income Tax (typ. 5%)
AT  = (AV + CD + RD + SD) × at_rate                 -- Advance Tax / advance VAT (typ. 5%)
TTI = CD + RD + SD + VAT + AIT + AT                 -- Total Tax Incidence
```
Specific (per-unit) duties supported: any component may be `max(ad_valorem, qty × specific_rate)`. Minimum/tariff values per HS (NBR minimum value list) override declared AV when higher — engine applies `AV_effective = max(declared_AV, tariff_value)` with a flag.

### SRO / Exemption Layer
`sro` (sro_no, year, description, validity) · `sro_benefit` (sro_id, hs_code pattern, component, mode: exempt/rate_override/cap, value, conditions jsonb — e.g., industrial IRC holder, bonded warehouse, capital machinery). Engine resolves applicable SROs by HS + tenant eligibility flags; applied benefits itemized on the calc breakdown (audit-ready).

### Cost vs. Recoverable Treatment (tenant policy + per-file override)
| Component | Default treatment |
|---|---|
| CD, RD, SD | Landed cost (always) |
| VAT | Registered manufacturer claiming rebate → recoverable (input VAT asset); trader/non-claimant → landed cost |
| AIT | Advance income tax asset (adjustable) → NOT landed cost by default |
| AT | Advance VAT asset (adjustable) → NOT landed cost by default |
Engine outputs both **landed-cost duty subtotal** and **recoverable-tax subtotal** per line.

## 6.2 Duty Structure Management & Tax Rate History
**Objectives:** authoritative dated rate store; zero hard-coded rates.
**Rules:** DS-01 rates effective-dated `(hs_code, component, rate, specific_rate, uom, effective_from, effective_to, source: finance_act/sro/manual, ref_doc)`; DS-02 maker-checker on all changes; DS-03 annual Finance Act import wizard (xlsx diff: changed lines highlighted, mass effective-dating July 1); DS-04 any calculation stores the **rate row ids used** (reproducibility — recalculate any historical consignment exactly).
**UI:** rate timeline per HS (stacked component chart), Finance Act diff review screen, SRO registry.
**Reports:** rate change impact (open POs/files affected), rate history audit.

## 6.3 Customs Duty / Regulatory / Supplementary / VAT / AIT / AT Tracking
Per-file, per-BoE-line ledger of each component across three stages: **system-computed → customs-assessed → paid** (challan). Differences:
- computed vs. assessed = `assessment_variance` (classification dispute, valuation uplift, SRO denial) → dispute workflow (doc 04 CC-03);
- assessed vs. paid should be zero; payment via challan records (challan_no, date, bank, amount per component).
AIT/AT tracked additionally in **advance tax sub-ledgers** per company per FY (additions per consignment, adjustments per return period entered by Tax Officer, closing balance) — answers "how much AIT have we accumulated this year?" instantly.

## 6.4 Customs Assessment Tracking
Covered structurally in doc 04 §4.17; analytics here: assessment uplift % by HS/port/C&F (valuation-risk map), query cycle time, dispute win rate, provisional releases outstanding (guarantee exposure).

## 6.5 Tax Payment Tracking
Challan register with scanned evidence; reconciliation: Σ challans per file = assessed TTI; unpaid-assessment aging (release blocker view); monthly duty payment summary by component for VAT return support (Mushak alignment exports: paid VAT/AT register xlsx).

---

# Landed Cost Management

## 6.6 Cost Elements & Allocation Model

### Element registry (extensible per tenant)
| Element | Default driver | Stagewise |
|---|---|---|
| Supplier goods value | direct (per line) | PI → CI |
| Freight (+surcharges) | weight (sea LCL/air) / volume / container-share (FCL) | quote → forwarder bill |
| Insurance premium | value | cover note → debit note |
| CD/RD/SD (+VAT/AIT/AT if cost) | direct per BoE line | computed → assessed → challan |
| Port & shipping-line charges, demurrage | container-share / weight | tariff est → receipts |
| C&F agency + documentation | per-BoE flat → value | rate card → bill |
| Bank charges (LC/TT/SWIFT), FX loss (policy) | value | event-driven actuals |
| Inspection (PSI), inland transport, misc | value / weight / flat | est → bills |

### Allocation drivers
`value` (CI line value), `qty`, `net_weight`, `gross_weight`, `volume(CBM)`, `container_share` (containers attributed to lines by stuffing plan), `direct` (already line-level, e.g., duty), `manual %`. Each element row chooses a driver; tenant defaults per element type; per-sheet override with reason.

### Cost Sheet Lifecycle
```
Draft (estimates seeded from feasibility snapshot)
→ Accumulating (actuals replace estimates as bills/challans land)
→ Ready (completeness check: all expected elements actual/accrued — Incoterm-aware expectation list)
→ Finalized (Finance Head approval) → posts InventoryCostUpdated + GL journals
→ Adjusted (post-finalization late bills → adjustment sheet version, delta postings)
```

### Calculation (per item line)
```
allocated(e, line) = element_amount(e) × driver_value(line) / Σ driver_value(all lines in element scope)
unit_landed_cost(line) = [ goods_value + Σ allocated(e, line) over cost elements ] / received_qty(line)
```
Scope: element may cover whole file, one shipment, or selected lines (e.g., demurrage on one container → only lines stuffed in it). Rounding: banker's rounding at 4 dp unit cost; residual pennies assigned to largest line (deterministic). Currency: every element converted at its own document FX rate; sheet reports both FCY detail and BDT totals.

## 6.7 Module Specs (template)
**Objectives:** true per-unit cost; estimate-vs-actual control; audit-ready breakdown.
**Business Rules:** LC-SHEET-01 sheet auto-created with import file; LCS-02 estimates seeded from Feasibility/Forecast engines; LCS-03 finalize blocked until completeness (IMP-06) — accrual entry allowed for missing bills (accrued element flagged, reversed on actual); LCS-04 received qty (GRN) is the divisor — shortage/damage qty policy: cost of lost units to expense or spread (tenant policy); LCS-05 every figure traceable to source doc id.
**Data:** `landed_cost_sheet` (file, version, status, totals, finalized_by/at) · `cost_element` (sheet, type, scope, driver, currency, amount, fx_rate, stage est/accrued/actual, source_doc) · `cost_allocation` (element, ci_line, driver_value, allocated_amt) · `line_landed_cost` (ci_line, item, received_qty, unit_cost, duty_cost, logistics_cost, finance_cost, total).
**UI:** Cost Sheet workspace — left: element list with stage chips & completeness meter; center: allocation matrix (lines × elements, drill to source); right: unit-cost cards with est-vs-actual delta bars; what-if panel (change driver → preview). Adjustment-version diff view.
**Journey:** auto-draft at file creation → Import Mgr reviews seeds → actuals stream in (events) → Ready → Import Mgr submits → Finance Head finalizes → postings fire → variance report vs. feasibility snapshot auto-generated.
**Workflow/Approval:** finalize: Import Mgr → Finance Head; adjustment versions same; driver overrides logged.
**Reports:** §6.8. **Notifications:** sheet Ready, finalize task, adjustment created, variance > x% vs. forecast.

## 6.8 Historical Cost Analysis & Product Cost Analysis
Marts (doc 10 §5): `fact_landed_cost` grain = file × item line, with element-type measures. Views: unit landed cost trend per item (with FX-normalized series), element mix waterfall (FOB → +freight → +duty → +port → unit cost), duty % of landed by HS, lane cost comparison (origin×port), supplier landed-cost ranking for same item (feeds vendor recommendation), forecast-vs-actual variance distribution (feeds engine recalibration), product profitability when selling price provided (margin per item per period).
