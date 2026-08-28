# 04 — Import Management Modules

The **Import File** is the unifying aggregate: one consignment's workspace linking PO(s), documents, finance instruments, shipment, clearance, costs, and receipt. File number format: `IMP-{COMPANY}-{FY}-{SEQ}`.

```
Import File states:
Planned → PO-Linked → PI-Accepted → Finance-Instrumented (LC/TT) → In-Production
→ Shipped → Documents-In-Bank → Documents-Released → At-Port → Under-Assessment
→ Duty-Paid → Released → In-Transit-Inland → Received → Costed → Closed
(+ Held / Disputed / Cancelled side-states)
```

---

## 4.1 Import Planning
**Objectives:** annual/quarterly import plan by item-category with budget, LC limit, and seasonality alignment; feeds feasibility baselines.
**Rules:** plan lines carry est qty, value (FOB & landed), target source country, target arrival window; plan vs. actual tracked automatically as files close; over-plan imports flagged to CFO.
**Data:** `import_plan` (fy, company, status, version) · `import_plan_line` (category/item, qty, est_fob, est_landed, target_month, source_country).
**UI:** plan grid with prior-year actuals side-by-side; coverage chart (planned vs. committed vs. received).
**Reports:** plan adherence, arrival calendar heatmap. **Approval:** plan & revisions → CFO. **Notifications:** monthly plan-vs-actual digest.

## 4.2 Import Purchase Order
Extension of PO (doc 03 §3.4) — adds: shipment tolerance (±% qty, partial-shipment & transshipment allowed flags mirroring LC terms), port pair, latest shipment date, inspection requirement (PSI agency), and links to created Import File. Rule IPO-01: LC terms must not contradict PO terms (validated at LC application — currency, amount±tolerance, latest shipment, ports, partial-shipment flag).

## 4.3 Proforma Invoice (PI) Management
**Objectives:** capture supplier PI as the basis for LC/TT and LCA.
**Rules:** PI-01 PI lines must reconcile to PO lines (qty/price tolerance check); PI-02 acceptance freezes the version used for LC (later supplier PIs become amendments); PI-03 validity tracking; PI-04 bank/beneficiary details on PI must match vendor master (fraud gate).
**Data:** `proforma_invoice` (file_id, vendor, pi_no, date, validity, currency, incoterm, payment_terms, bank_details jsonb, status, version) · `pi_line` (po_line, qty, price).
**UI:** PI entry with PO-diff panel; version compare. **Workflow:** accept = Import Mgr; deviation from PO > tolerance → Procurement Head. **Notifications:** validity expiry T-7.

## 4.4 Commercial Invoice (CI) & 4.5 Packing List (PL) Management
**Rules:** CI-01 CI reconciles to PI/PO and later to Bill of Entry; CI-02 PL cartons/weights must total CI quantities; net/gross weight & CBM captured per line (drives weight/volume cost allocation); CI-03 discrepancy log vs. LC terms (basis for bank discrepancy handling); CI-04 multiple CIs per file supported (partial shipments) — each spawns a Shipment record.
**Data:** `commercial_invoice` (file, shipment_id, ci_no, date, currency, amount, status) · `ci_line` (pi_line, qty, price, net_wt, gross_wt) · `packing_list` (pl_no, total_cartons, gross_wt, net_wt, cbm) · `pl_line` (carton range, item, qty, weights, dims).
**UI:** document workspace with side-by-side PO/PI/CI/PL reconciliation grid (auto-highlight variances); OCR-assisted entry from PDF.

## 4.6 HS Code Management
**Objectives:** authoritative, dated HS classification driving duty computation.
**Rules:** HS-01 BD 8-digit tariff lines preloaded from NBR tariff schedule, updated each Finance Act (effective-dated); HS-02 item↔HS mapping at item master with confidence/approved-by; HS-03 per-consignment override allowed (logged; assessment may differ — variance tracked); HS-04 duty rates (CD/RD/SD/VAT/AIT/AT %, specific duties, SRO exemptions) live on `duty_rate` rows keyed (hs_code, effective_from) — doc 06.
**UI:** HS browser (chapter tree + search), item mapping queue, rate timeline view per HS.
**Reports:** classification coverage, items with disputed classifications, rate-change impact (items affected by new Finance Act).
**Approval:** mapping changes maker(Tax Officer)-checker(Finance Head).

## 4.7 Import Permit & 4.8 Country of Origin Management
**Permits:** registry of consignment-level/period permits — DGDA block-list/permit (pharma), BSTI clearance (food/consumer), quarantine (agro), explosives (chemicals), IRC ceiling tracking. Rule PM-01: file cannot advance to LC stage if its category requires a permit that is missing/expired; PM-02 permit utilization ledger (qty/value drawn per consignment).
**COO:** certificate capture (form type: generic CO, SAFTA/SAPTA/APTA, China-Bangladesh FTA) with preferential-rate eligibility flag feeding the duty engine (preferential CD where COO form valid). Expiry/issuer registry; mismatch with CI origin → alert.

## 4.9 Shipment Planning & 4.10 Shipping Schedule Management
**Objectives:** plan consignment movement (mode, route, carrier, ETD/ETA) and maintain live schedule.
**Rules:** SH-01 shipment created per CI (or planned pre-CI); SH-02 milestone chain by mode — Sea: booking → stuffing → gate-in → ETD/ATD → transshipment(s) → ETA/ATA → discharge → IGM; Air: booking → ATD → ATA; SH-03 ETA changes versioned; slippage > x days notifies planner & feeds delay-prediction training data; SH-04 latest-shipment-date (LC) breach risk alert at T-7.
**Data:** `shipment` (file, mode sea/air/land, carrier, vessel/flight, voyage, pol, pod, etd, eta, atd, ata, status) · `shipment_milestone` (type, planned_at, actual_at, source manual/api) · `shipment_eta_revision`.
**UI:** shipment board (kanban by milestone), arrival calendar, map view (port markers), per-file timeline.
**Notifications:** ETD/ETA changes, LC latest-shipment risk, arrival T-5/T-2 (triggers clearance prep checklist).

## 4.11 Freight Cost Management
Freight quotes per shipment (forwarder, buy rate, currency, surcharges: BAF/CAF/THC/DO/AMS), booking confirmation, freight invoice capture → cost element on file (estimated → actual). Rule FR-01: estimated freight auto-suggested from lane history (Cost Forecasting Engine); FR-02 forwarder bills reconciled line-by-line vs. quote, variances need Import Mgr approval. Reports: lane rate trends, forwarder comparison, surcharge analysis.

## 4.12 Container Tracking
`container` (shipment, container_no ISO 6346 check-digit validated, size/type, seal, gross_wt, status) + event stream (gate-out empty, stuffed, gate-in, loaded, discharged, gate-out full, empty return). MVP: manual/C&F-portal updates + bulk import; Enterprise: aggregator API (project44/Vizion class) webhook ingestion via EventBridge. Demurrage/detention clocks per container (port free days vs. line free days) with cost projection. Reports: dwell time per port, demurrage incurred vs. avoided, container cost analysis (doc 08).

## 4.13 Bill of Lading & 4.14 Air Waybill Management
**Rules:** BL-01 capture MBL/HBL (sea) or MAWB/HAWB (air) with shipped-on-board date (drives LC latest-shipment compliance check), freight prepaid/collect, consignee/notify (must match LC), original count or telex/surrender/e-BL status; BL-02 bank endorsement status tracked (BL released by bank → C&F can file BoE); BL-03 amendments logged with charge capture.
**Data:** `transport_document` (shipment, type mbl/hbl/mawb/hawb, number, issue_date, on_board_date, freight_terms, consignee, notify, original_count, surrender_status, endorsed_at, custody bank/c&f/customs).
**UI:** document timeline showing custody chain (supplier → bank → importer → C&F → customs).

## 4.15 Insurance Management
Marine cover note before LC (BD requirement): `insurance_policy` (file, insurer, cover_note_no, policy_no, type marine/air/land, insured_value = CI×110% default, premium, vat_on_premium, open_cover_id?). Open-cover support: master policy with per-shipment declarations and aggregate utilization. Claims sub-ledger (survey, lodged, settled). Premium becomes a landed-cost element. Alerts: cover expiring before revised ETA.

## 4.16 Incoterms Management
Incoterms 2020 master with **cost-responsibility matrix** (who pays: export clearance, main carriage, insurance, import clearance, inland) — drives which cost elements the landed-cost engine expects per file and how bid normalization adds missing legs (doc 03 §3.3, doc 07). Validation: EXW/FOB require importer freight+insurance records; CIF/CIP expect them embedded in CI value (engine prevents double counting).

## 4.17 Customs Clearance & 4.18 Port Clearance Management
**Objectives:** model the BoE-to-release pipeline at Chattogram/Mongla seaports, ICDs, Benapole/other land ports, and HSIA air cargo.
**Rules:**
- CC-01 clearance checklist auto-generated per file (mode/port/category aware): endorsed BL, CI, PL, COO, cover note + money receipt, LCA copy, IRC, BIN, permits, prior assessment references.
- CC-02 Bill of Entry record mirrors ASYCUDA fields: BoE no/date, office code, declarant (C&F AIN), assessable value per line, duty lines per type; status: submitted → queried → assessed → paid → examined (lane G/Y/R) → released.
- CC-03 system-computed duty vs. assessed duty variance per line; > tolerance → dispute record (query response / appeal / provisional release under bank guarantee — tracked with guarantee register).
- CC-04 port stage: delivery order, port dues/charges capture, examination findings, gate-out; demurrage accrual auto-computed daily after free time.
- CC-05 every clearance milestone timestamps for SLA analytics (BoE→assessment, assessment→payment, payment→release).
**Data:** `bill_of_entry` (file, boe_no, date, customs_office, cnf_id, status, lane, total_av, total_duty jsonb) · `boe_line` (ci_line, hs_code_declared, av, duty breakdown cols) · `assessment_variance` (line, type, system_amt, assessed_amt, status, resolution) · `clearance_milestone` · `port_charge` (type, amount, receipt_ref) · `bank_guarantee` (purpose, amount, expiry, status).
**UI:** clearance cockpit (file queue by stage & port, aging colors), BoE entry/import (paste/CSV from ASYCUDA print), variance review screen, demurrage watchlist.
**Reports:** clearance cycle by port/C&F, lane distribution, query/dispute log, demurrage analysis.
**Notifications:** query raised, assessment ready, variance > tolerance, free-time 70%/100%, released.
**Approval:** dispute escalation (Tax Officer → Finance Head), provisional release guarantee (CFO).

## 4.19 C&F Agent & 4.20 Customs Broker Management
Agent master (AIN license no, ports covered, rate card: agency fee structure — per BoE/per container/% of value, documentation charges), file assignment & portal access (assigned files only), performance scorecard (avg clearance days, query rate, demurrage incidence, billing accuracy), charge-bill capture with rate-card auto-verification (variances flagged). Rule CF-01: one active agent per file per port; reassignment logs custody transfer of documents.

## 4.21 Import Cost Tracking
The file's **cost ledger**: every element `(cost_type, stage estimated|committed|actual, currency, amount, fx_rate, source_doc, allocatable?)` — supplier value, freight, insurance, CD/RD/SD/VAT/AIT/AT, port, C&F, bank charges (LC opening/amendment/acceptance/SWIFT), inspection, inland transport, misc. This ledger is the single input to the Landed Cost Engine (doc 06/07). UI: file cost sheet with estimated-vs-actual columns and completeness indicator (IMP-06 gate). Report: cost element trend per item/lane.

---

### Module Journey — one file end-to-end (swimlane summary)
Buyer (PO+feasibility) → Import Mgr (file, PI, insurance, LCA→LC doc 05) → Supplier (ship, docs) → Bank (scrutiny, endorsement) → C&F portal (BoE, assessment, duty challans, release) → Transporter → Store (GRN doc 08) → Import Mgr + Finance (cost sheet finalize) → system (valuation, GL, scorecards, analytics).
