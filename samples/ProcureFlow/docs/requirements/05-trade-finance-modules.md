# 05 — Trade Finance Modules

## 5.1 Letter of Credit Management

### Objectives
Full LC lifecycle: application → issuance → amendments → document presentation → acceptance/payment → closure; live exposure, margin, and maturity visibility per bank and per company.

### Business Rules
- LC-01 Prerequisites (BD): accepted PI, insurance cover note + money receipt, valid IRC with available ceiling, LCA form data complete, HS classification done, permit check passed (doc 04 §4.7).
- LC-02 LC types: sight, usance (deferred 30/60/90/120/180/360), UPAS (usance payable at sight — supplier paid at sight via financing bank, importer pays at maturity + interest); transferable/back-to-back flags.
- LC-03 Term consistency validation vs. PO/PI: amount (±tolerance %), currency, latest shipment date, expiry (≥ latest shipment + presentation period, default 21 days), ports, partial shipment & transshipment flags, Incoterm, documents required list.
- LC-04 Margin: % per bank arrangement captured at opening; margin amount blocked → tracked as restricted funds; released/adjusted at retirement; margin top-up events on amendments.
- LC-05 Exposure ledger per bank facility: limit, outstanding (open LCs + accepted bills + loans), available — application blocked if facility insufficient (override: CFO).
- LC-06 Document presentation: bank's discrepancy notices logged (discrepancy code list), accept/refuse decision workflow; acceptance creates **maturity obligation** (bill) with due date = on-board/sight basis per tenor.
- LC-07 Retirement options at maturity: own funds, import loan (LTR/MPI — §5.6), or partial; FX deal rate captured → realized FX difference computed vs. booking rate.
- LC-08 All bank charges (opening commission, amendment, acceptance, SWIFT, confirmation, handling) captured per event → flow to file cost ledger as bank-charge elements.
- LC-09 Expiry/shipment-date watchdogs: alerts T-15/T-7/T-3; expired-undrawn LC closure workflow releasing margin & facility.

### Data Model
`bank` · `bank_facility` (company, bank, type LC/loan/guarantee, limit, currency, expiry, outstanding) · `letter_of_credit` (file_id, lc_no, bank, facility, type, tenor_days, currency, amount, tolerance_pct, issue_date, expiry, latest_shipment, margin_pct, margin_amount, status: applied/issued/amended/documents_presented/accepted/retired/closed/cancelled) · `lc_amendment` (seq, fields_changed jsonb, value_delta, charges, status) · `lc_presentation` (set_no, documents jsonb, discrepancies jsonb, decision, accepted_at, maturity_date, bill_amount) · `lc_charge` (event, type, amount) · `lc_margin_event` (block/top-up/release, amount) · `lc_retirement` (source own/loan/mixed, fx_rate, amounts).

### UI Screens
LC register (status, bank, maturity timeline); LC 360 (terms, amendments diff viewer, presentation sets, margin ledger, charges, linked file); application wizard (pulls PI/insurance/PO data, validation panel); maturity calendar (month wall with daily totals); bank exposure dashboard (facility utilization gauges).

### User Journey
Import Mgr opens application from file → wizard validates LC-01/03 → CFO approves → marked Applied; bank issues → LC no/SWIFT ref recorded, margin event posted → shipment docs presented → discrepancies logged & decided → acceptance creates bill with maturity → T-7/3/1 alerts → retirement entry (funding source, FX) → margin released → charges finalized → LC closed → file cost ledger updated.

### Workflow / Approval
Application: Import Mgr → CFO (always). Amendment: value/tenor-increasing → CFO; clerical → Import Mgr. Discrepancy acceptance: Import Mgr (+CFO if payment-impacting). Retirement with loan: CFO.

### Reports
LC outstanding by bank/company, maturity ladder (next 30/60/90 days cash needs), margin blocked summary, amendment frequency & cost, discrepancy Pareto, UPAS interest cost analysis, FX gain/loss on retirements.

### Notifications
Issuance, each amendment, presentation received, discrepancy decision task, maturity T-7/3/1 (non-suppressible to CFO + Import Mgr), expiry watchdogs, facility ≥ 85% utilization.

## 5.2 Back-to-Back LC (RMG/Textile)
- B2B-01 Master export LC (or sales contract) registered with value & expiry; B2B import LCs open **against** it within entitlement: default fabric/accessories ≤ 75% of export LC value for woven (tenant-configurable per policy/bond terms), knit per net-FOB rules — entitlement engine computes available headroom per master.
- B2B-02 Maturity alignment check: B2B usance maturity should fall after expected export realization date; misalignment warning (liquidity risk).
- B2B-03 Utilization ledger: master LC ↔ list of B2B LCs with consumption %, plus realization tracking (export proceeds) → settlement matching.
- Data: `master_export_lc` (lc_no, buyer, bank, value, expiry, realized_value) · `b2b_link` (master_id, import_lc_id, entitlement_used). UI: master 360 with entitlement waterfall. Reports: entitlement utilization, maturity vs. realization gap. Approval: B2B opening validates entitlement; override → CFO.

## 5.3 Bank Contract Management (non-LC import contracts)
For TT-based imports under bank "contract" registration: `import_contract` (bank ref, supplier, value, validity, IMP form refs) with utilization vs. remittances; ensures Bangladesh Bank reporting alignment (IMP matching against shipments). Alerts on unmatched remittances aging > policy days.

## 5.4 TT Payment Management & 5.5 SWIFT Reference Tracking
- TT-01 advance/partial/balance schedule against PO/PI within regulatory advance limits (configurable; warning + CFO approval beyond policy).
- TT requests generate bank instruction pack (purpose code, beneficiary from vendor master — immutable match rule, supporting docs); status: requested → executed (value date, FX rate, charges) → shipment-matched.
- `swift_message` registry: MT103/202 (payments), MT700/707/750/754/756 (LC chain) references linked to LC/TT entities — single thread view of a transaction's bank trail; unmatched-reference inbox for reconciliation.
- Reports: advance exposure (paid, not shipped), TT vs. LC mix, charge comparison by bank.

## 5.6 Import Loan Management
Loan types: LTR (Loan against Trust Receipt), MPI/Murabaha Post Import (Islamic), time loan — created at LC retirement or post-clearance: `import_loan` (lc_id/file_id, bank, type, principal, rate/profit, start, tenor, schedule jsonb, outstanding, status). Interest/profit accrual computed daily (simple/compound per bank), repayment entries reduce outstanding; cost flag: interest may be expensed or (policy) included in landed cost for long-tenor — tenant accounting policy, default expense. Alerts: installment T-5, overdue, rate reset. Reports: loan outstanding by bank, weighted avg cost of import finance, interest expense per file/item.

## 5.7 Margin Tracking
Cross-instrument restricted-cash view: all `lc_margin_event` + guarantee margins aggregated by bank/company; aging of blocked funds; released-vs-blocked trend; opportunity-cost estimate (margin × avg deposit rate) on dashboard — a CFO-facing widget unique in market.

## 5.8 LC Amendment Management
Covered in LC-amendments above; adds amendment **reason taxonomy** (shipment delay, qty change, price change, document change, expiry extension) feeding analytics: top amendment causes → process fixes (e.g., chronic latest-shipment extensions for a supplier lowers its scorecard responsiveness metric).

## 5.9 Payment Schedule Management
Unified obligation calendar across instruments: LC maturities, TT schedule lines, loan installments, vendor invoice due dates → `payment_obligation` (source_type, source_id, due_date, currency, amount, status, paid_ref). Cash-requirement forecast by week/currency; export to xlsx for treasury. Notifications: weekly treasury digest; per-obligation T-7/T-3. Approval: schedule changes on TT → CFO.

---

### Domain Notes for Engine Integration
- Every finance event that carries cost (charges, interest-in-cost, FX loss policy) emits `CostElementRecorded` to the file cost ledger (doc 04 §4.21) → consumed by Landed Cost Engine.
- `BillAccepted`, `LoanCreated`, `MarginBlocked` events update the bank-exposure read model in near-real-time (doc 09 EDA).
