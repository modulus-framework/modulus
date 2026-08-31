# 03 — Core Procurement Modules

Template applied to every module: **Objectives · Business Rules · Data Model · UI Screens · User Journey · Workflow · Reports · Notifications · Approval**. Deep detail for anchor modules; pattern-siblings list deltas.

---

## 3.1 Vendor Management (anchor: Supplier Registration → Qualification → Evaluation → Scorecard)

### Objectives
Single vendor master per tenant with lifecycle states; risk-aware approved-vendor lists per category; automatic performance capture; portal self-service.

### Business Rules
- VR-01: Lifecycle: `Prospect → Registered → Under Qualification → Qualified → Active → On Hold → Blacklisted → Archived`. Only `Active` vendors selectable on RFQ/PO (override = Procurement Head approval).
- VR-02: Duplicate detection on (TIN | BIN | bank account | normalized name+country); merge tool preserves history.
- VR-03: Mandatory KYC by vendor type — local: trade license, TIN, BIN/VAT reg, bank proof; foreign: registration cert, bank (SWIFT), beneficiary name must match LC docs later (hard validation at LC application).
- VR-04: Bank account add/change = maker-checker + auto-notification to vendor's registered email (fraud control).
- VR-05: Qualification per **category** (a vendor may be Qualified for "Dyes & Chemicals" but not "Machinery"); expiry & re-qualification cycle (default 24 months).
- VR-06: Blacklisting requires reason code + approver; blocks new documents, not in-flight settlements.
- VR-07: Scorecard auto-computed monthly: OTD 35%, Quality (acceptance rate) 30%, Price competitiveness vs. category index 15%, Responsiveness (RFQ turnaround) 10%, Compliance (doc accuracy, audit findings) 10%. Grades A ≥ 85, B ≥ 70, C ≥ 55, D < 55 (two consecutive D → auto On-Hold proposal).

### Data Model (core tables; full DDL in doc 10)
`vendor` (id, tenant_id, code, legal_name, type local/foreign, country, status, risk_grade, tax_ids jsonb, default_currency, payment_terms, incoterm_default) · `vendor_contact` · `vendor_address` · `vendor_bank_account` (maker/checker cols) · `vendor_category` (vendor_id, category_id, qualification_status, qualified_until) · `vendor_document` (type, s3_key, expiry) · `vendor_qualification` (questionnaire jsonb, score, decision) · `vendor_evaluation_event` (po_id, metric, value) · `vendor_scorecard` (vendor_id, period, otd, quality, price, response, compliance, total, grade).

### UI Screens
Vendor list (status/risk/category facets) · Vendor 360 (header KPIs: total spend, open POs, score trend, risk badge; tabs: profile, categories, documents, bank, transactions, scorecards, notes) · Registration wizard (internal + portal variants) · Qualification workspace (checklist, questionnaire scoring, site-audit attachment) · Scorecard dashboard · Merge tool.

### User Journey (qualification)
Buyer invites vendor → vendor completes portal registration + uploads KYC → Vendor Manager screens (auto-checks: duplicate, sanctions list, doc expiry) → category questionnaire scored → optional site audit recorded → decision task → on approve: status Qualified, added to category AVL, vendor notified.

### Workflow
`vendor-qualification` definition: screen → score → (site-audit if strategic category) → Vendor Mgr → Procurement Head (strategic only). Re-qualification auto-spawned 60 days before expiry.

### Reports
Vendor master audit, AVL by category, qualification pipeline, scorecard league table, expiring documents, blacklist register.

### Notifications
Vendor: registration received/approved, doc expiring, score published. Internal: qualification task, doc expiry (T-30), bank change checker task, auto On-Hold proposal.

### Approval
Qualification (above), bank change (maker-checker), blacklist (Procurement Head + CFO if open balance).

---

## 3.2 Purchase Requisition (PR)

**Objectives:** controlled demand capture from departments/MRP with budget gate.
**Business Rules:** PR-01 line-level need-by date ≥ today + category lead time warning; PR-02 budget reservation at submit (soft-block; hard-block configurable); PR-03 item or free-text lines (free-text requires category for budget mapping); PR-04 consolidation: buyers can merge PR lines into one sourcing case/PO; PR-05 cancellation releases reservations.
**Data Model:** `purchase_requisition` (no, company, dept, cost_center, requester, status, total_est) · `pr_line` (item_id?, description, qty, uom, est_price, need_by, budget_line_id, status: open/sourced/po’d/cancelled).
**UI:** My PRs, PR form (item picker w/ stock & last-price hints), approval inbox, buyer's demand workbench (open PR lines, group→RFQ/PO).
**Journey:** requester drafts → submits → DoA chain → approved lines appear in demand workbench.
**Workflow:** `pr-approval` per DoA slab; budget-fail branch to Budget Owner.
**Reports:** PR cycle time, open demand by category, PR→PO conversion, budget reservation aging.
**Notifications:** approval tasks, budget failure, line sourced/PO created (to requester).
**Approval:** DoA §6 doc 02.

## 3.3 RFQ Management / RFP Management / Quotation Comparison / Bid Analysis

**Objectives:** structured competitive sourcing with auditable award rationale.
**Business Rules:**
- RFQ-01 minimum-bidder policy by amount (default 3 > 5L); sealed mode hides bids until deadline; late bids flagged, acceptance needs Sourcing Mgr approval.
- RFQ-02 invited vendors must be Qualified for the category (override path).
- RFQ-03 RFP adds weighted technical/commercial envelopes (e.g., 60/40); technical scoring committee with quorum; commercial opened only for technically qualified bidders (score ≥ cutoff).
- RFQ-04 Comparison normalizes to **landed-cost basis** for imports: bid price + estimated freight (Incoterm-aware) + duty cascade (HS-based) + handling — powered by the Cost Forecasting Engine (doc 07) so FOB-vs-CIF bids compare fairly.
- RFQ-05 Award ≠ lowest landed TCO requires justification + CFO co-approval.
- RFQ-06 Full bid tab snapshot frozen at award (immutability for audit).
**Data Model:** `sourcing_case` (type RFQ/RFP, status, deadline, sealed, weights jsonb) · `sourcing_line` (links pr_line) · `sourcing_invitation` (vendor, sent/viewed/responded) · `bid` (vendor, currency, validity, incoterm, payment_terms, status) · `bid_line` (price, lead_time, moq, alt_flag) · `bid_score` (criterion, evaluator, score, remarks) · `award` (winner(s), split %, rationale, snapshot jsonb).
**UI:** sourcing case builder, vendor portal bid form (line grid + attachments + Q&A thread), sealed-bid countdown, comparison matrix (per line: price, landed est, lead time, score; heat-mapped; split-award allocator), technical scoring sheets, award summary.
**Journey (RFQ):** buyer creates case from PR lines → invites AVL vendors → portal bids → deadline auto-locks → comparison matrix auto-built with landed estimates → recommendation → award workflow → PO auto-drafted from award.
**Workflow:** publish (Sourcing Mgr) → [RFP: technical committee parallel scoring, quorum] → award per DoA.
**Reports:** savings vs. baseline (last price/budget), participation rate, cycle time, award concentration, justification register.
**Notifications:** vendor invite/deadline T-24h/result; internal: bids-in count, scoring tasks, award task.
**Approval:** award per DoA; late-bid acceptance; non-lowest award co-approval.

## 3.4 Purchase Order (PO)

**Objectives:** the legal commitment document; the feasibility gate for imports; the spine linking PR → sourcing → import file → GRN → invoice.
**Business Rules:**
- PO-01 sources: from award, from contract call-off, from PR direct (within DoA), manual (lowest privileges).
- PO-02 import PO requires: foreign vendor, Incoterm, currency, HS code per line, port of loading/discharge, payment mode (LC/TT/contract) — these feed feasibility + import file creation.
- PO-03 **Feasibility gate** (import): on submit, engine returns score 0–100 + breakdown; below tenant threshold (default 60) → blocked pending CFO override with reason. Snapshot stored on PO (immutable evidence of decision basis).
- PO-04 revisions version the PO (R0, R1…); value-increasing revisions re-enter approval; dispatched revisions notify vendor and require acknowledgement.
- PO-05 budget consumption moves reservation→commitment at approval.
- PO-06 closure: auto when fully received+matched; force-close requires reason + releases residual commitment.
**Data Model:** `purchase_order` (no, rev, company, vendor, type domestic/import/service, currency, incoterm, payment_mode, status, totals, feasibility_score, feasibility_snapshot jsonb, contract_id?) · `po_line` (item, desc, hs_code, qty, uom, unit_price, need_by, received_qty, invoiced_qty, budget_line) · `po_event` (status history) · `po_acknowledgement`.
**UI:** PO workbench (status lanes), PO form with **Feasibility panel** (score gauge, predicted landed cost/unit, margin %, timeline, top risk factors, comparison to last 5 imports of same item), revision diff view, vendor acknowledgement portal page, print/PDF (tenant letterhead).
**Journey (import):** buyer converts award → completes import fields → Analyze (engine, <3 s) → reviews panel → submit → DoA chain (gate enforces score) → approve → dispatch (email+portal) → vendor acknowledges → import file auto-created (doc 04).
**Workflow:** `po-approval` (doc 02 §7 sample).
**Reports:** open PO aging, commitment vs. budget, revision frequency, feasibility score vs. actual outcome (model calibration report), maverick spend.
**Notifications:** approval tasks, gate failure, dispatch/ack, need-by risk (vendor unconfirmed at T-x).
**Approval:** DoA; feasibility override.

## 3.5 Contract Management
**Objectives:** rate agreements & framework contracts with call-off control.
**Rules:** validity window + value/qty caps; price lists per item with stepped/volume pricing; expiry alerts T-60/T-30; off-contract purchase of contracted item → maverick flag; renewal clones with redline diff.
**Data:** `contract` (vendor, type rate/framework/service, start/end, cap_value, consumed_value, status) · `contract_line` (item, price, min_qty, escalation jsonb) · `contract_document` · `contract_milestone` (deliverables/SLA for service contracts).
**UI:** repository w/ full-text search (OpenSearch over OCR'd PDFs), contract 360 (consumption gauge), call-off from contract → PO.
**Reports:** consumption vs. cap, expiring contracts, realized contract savings, maverick spend.
**Approval:** contract per DoA (CFO above threshold); renewals same path.

## 3.6 Invoice Management & 3-Way Match
**Rules:** capture (manual / portal / email-in with OCR assist) → match PO↔GRN↔Invoice at line level with tolerances (doc 01 §4) → exceptions queue → AP voucher on pass; duplicate check (vendor+invoice_no+amount); import vendor invoices reconcile against CI; service POs 2-way match with completion certificate.
**Data:** `vendor_invoice` (vendor, po_id, type goods/service/charge, currency, amount, tax jsonb, status, match_status) · `invoice_line` (po_line, grn_line, qty, price, variance) · `match_exception` (type, amount, resolution, approver).
**UI:** capture inbox, match workbench (3-pane PO/GRN/Invoice with auto-line pairing), exception board.
**Reports:** unmatched aging, exception Pareto, DPO, accrual list (GRN-not-invoiced).
**Approval:** exceptions > tolerance → Finance Head; credit notes maker-checker.

## 3.7 Goods Receipt (procurement view)
Summary here; full warehouse detail in doc 08. GRN against PO/ASN; over-receipt tolerance; QC hold→accept/reject/deviation; rejected qty auto-creates return/debit-note draft; GRN posts accrual (GR/IR) and feeds vendor OTD/quality metrics.

## 3.8 Budget Control
**Rules:** budgets per FY × cost center × category (optionally project); lifecycle reserve(PR) → commit(PO) → consume(GRN/Invoice per tenant policy); revisions versioned with approval; hard/soft block per category; multi-year project budgets supported.
**Data:** `budget` (fy, company, status, version) · `budget_line` (cost_center, category, amount, reserved, committed, consumed) · `budget_transaction` (type, source_doc, amount, balance_after — append-only ledger).
**UI:** budget grid (entry/import xlsx), utilization heat map, drill-through to documents, revision diff.
**Reports:** utilization, forecast-to-spend (run-rate), over-budget exceptions, reservation aging.
**Approval:** budget + revisions: Budget Owner → CFO.

## 3.9 Spend Analysis & Procurement Analytics
Classification: every PO line mapped to category taxonomy (UNSPSC-lite, tenant-extendable); auto-classification suggestions (ML, doc 07). Cubes: spend by category/vendor/BU/site/month; price variance (PPV) vs. last/contract/standard; savings tracker (negotiated vs. baseline); tail-spend identification (bottom 80% vendors by spend); single-source risk exposure. Served from the reporting replica + materialized marts (doc 10 §5), visualized in dashboard module (doc 08).
