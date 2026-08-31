# TradeFlow — Consolidated Business Rule Register

**Version:** 1.0 | **Date:** August 2026
**Suite:** [README](README.md) · [BRS-Core](BRS-Core.md) · This file · [Phasing & Implementation](BRS-Phasing-Implementation.md)

Every business rule from S-v1 (docs 01–08) and S-v2 (V01–V04) consolidated into one namespace. Original IDs are preserved in the **Source** column for traceability. Validation rules and exception paths live with each rule; edge cases follow BRS-Core §5.6 charter.

**Phases:** P1 = MVP (months 0–8) · P2 = months 9–18 · P3 = months 19–36.

---

## 1. Platform Rules

### 1.1 Multi-tenancy (BR-MT)
| ID | Rule | Source | Module | Phase |
|---|---|---|---|---|
| BR-MT-01 | No API path may accept tenant_id from client payload — derived from token only | V03 MT-01 | Platform | P1 |
| BR-MT-02 | Background jobs carry tenant context explicitly; fan-out jobs iterate tenants with per-tenant transactions | V03 MT-02 | Platform | P1 |
| BR-MT-03 | Cross-tenant analytics only via anonymized pipelines with tenant opt-in | V03 MT-03 | Intelligence | P3 |
| BR-MT-04 | Tenant export = complete + machine-readable (JSON lines + attachments) ≤ 72 h | V03 MT-04 | Platform | P2 |
| BR-MT-05 | Deletes are soft + retention-scheduled; crypto-shred tenant DEK at retention end | V03 MT-05 | Platform | P2 |
| BR-MT-06 | Row-Level Security enforced on every tenant table; app role has NOBYPASSRLS; migrations role separate | V03 A2 | All | P1 |
| BR-MT-07 | Tenant overrides of platform reference data stored as deltas, never mutations of platform rows | V03 A4 | Platform | P1 |

### 1.2 Organization (BR-ORG)
| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-ORG-01 | Documents stamp company/bu/site/dept at creation; validated against org-tree version | V03 ORG-01 | P1 |
| BR-ORG-02 | Approval routing resolves "manager-of / head-of / position-in-ancestor-chain" as-of document creation date | V03 ORG-02 | P1 |
| BR-ORG-03 | Reorg = new tree version; in-flight workflows keep resolution snapshots; new documents use the new version | V03 ORG-03 | P1 |
| BR-ORG-04 | Cross-company transactions require inter-company flag → inter-company billing instruction | V03 ORG-04 | P3 |
| BR-ORG-05 | Sites carry customs attributes (nearest port, bonded-warehouse license no) | V03 ORG-05 | P1 |
| BR-ORG-06 | Vacant position in approval chain: auto-skip to next level + admin alert (configurable hard-stop) | V03 B4 | P1 |
| BR-ORG-07 | User holding multiple positions: union of permissions; routing per document's org stamp | V03 B4 | P1 |

### 1.3 Identity & sessions (BR-IDM)
| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-IDM-01 | BFF pattern: tokens server-side only; HttpOnly SameSite session cookie | V03 C1 | P1 |
| BR-IDM-02 | User lifecycle: invite → activate (12+ char password + breach check, or SSO-only) → suspend (immediate session revoke) → offboard (reassignment wizard) → anonymize after retention | V03 C2 | P1 |
| BR-IDM-03 | MFA: TOTP baseline; WebAuthn preferred; SMS fallback rate-limited; per-tenant/role enforcement | V03 C3 | P1 |
| BR-IDM-04 | Risk-based step-up: new device, IP-country change, high-value approval > BDT threshold → re-auth | V03 C3 | P2 |
| BR-IDM-05 | SSO roles never auto-mapped from IdP — explicit admin-approved mapping table | V03 C4 | P2 |
| BR-IDM-06 | SCIM sync: create/suspend only; role changes stay in-app | V03 C4 | P3 |
| BR-IDM-07 | IP allowlists scoped (tenant / role / API client); time-boxed travel exceptions; denials audited with geo | V03 C5 | P2 |
| BR-IDM-08 | Session registry: sliding idle 30 min, absolute 12 h, concurrent-session policy per role, device list + remote revoke, admin kill switch | V03 C6 | P1 |
| BR-IDM-09 | Refresh-token rotation with reuse detection → family revocation | V03 C6 | P1 |
| BR-IDM-10 | High-value actions (bank-account approval, DoA edits) always re-prompt credentials | V03 C6 | P1 |

### 1.4 Permissions (BR-PM)
| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-PM-01 | Permission = (capability, scope, conditions); ~320-capability registry; scopes: org subtree, company, category, site, amount ceiling, own-vs-all | V03 D1 | P1 |
| BR-PM-02 | Permission set compiled at login, cached, invalidated on assignment/role/org change; server re-checks every command; UI hints never authority | V03 D4 | P1 |
| BR-PM-03 | Creator ≠ approver on approval steps — enforced by workflow engine | V03 D5 / V04 A5 | P1 |
| BR-PM-04 | Maker-checker pairs on money-adjacent masters: vendor bank, duty rates, FX rates, credit notes, workflow publish, DoA edits | V03 D1 | P1 |
| BR-PM-05 | Delegation: time-boxed, capability subset with ceiling, "on behalf of" stamp, auto-expiry, no re-delegation | V03 D5 | P1 |
| BR-PM-06 | SoD conflict matrix per tenant; overrides need Admin + Compliance dual approval → exception report | V03 D5 | P2 |
| BR-PM-07 | Auditor read-all excludes field-encrypted values without `audit.unmask` (dual-control, alarmed) | V03 D6 | P2 |
| BR-PM-08 | Firecall access: two-admin activation, 4 h auto-revoke, session-recording flag | V03 D6 | P3 |

### 1.5 Workflow engine (BR-WF)
| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-WF-01 | Definitions are versioned JSON; in-flight instances pin to their version forever | V04 WF-01 | P1 |
| BR-WF-02 | Document edited during Return→Resubmit changing the amount slab → chain re-resolves from step 1 | V04 WF-02 | P1 |
| BR-WF-03 | Step types: approval, approval-chain (DoA), condition (sandboxed expression), system-action, timer, wait-event, sub-workflow, human-task, parallel (all/any/quorum), notification | V04 A3 | P1 core 6; P2 rest |
| BR-WF-04 | Assignment resolvers (position, role, manager-of, head-of, doa, round-robin, least-loaded, named-user); resolution snapshot stored on task | V04 A4 | P1 |
| BR-WF-05 | Decisions: Approve / Reject / Return (comment mandatory) / Reassign (capability-gated) / Request-info (pauses SLA) | V04 A5 | P1 |
| BR-WF-06 | Task claim atomic; double-act returns friendly conflict | V04 A5 | P1 |
| BR-WF-07 | Recall allowed while first step pending; later recall needs `workflow.recall` capability + notifies approvers | V04 A5 | P1 |
| BR-WF-08 | SLA: reminders at 50%/80%, escalation chain, final timeout action (auto-reject default; auto-approve only with explicit risk-ack, low-value only) | V04 A6 | P1 |
| BR-WF-09 | Working-hours-aware SLA clocks (tenant business calendar; Ramadan hours) | V04 A6 | P2 |
| BR-WF-10 | Publish requires schema-valid save + simulation-run receipt + checker approval | V04 A7 | P1 |
| BR-WF-11 | Every transition appends `workflow_event`; instance state is a replay-safe projection | V04 A5 | P1 |

### 1.6 Notifications (BR-NTF)
| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-NTF-01 | Severity → channel map: Info→in-app; Normal→in-app+email; High→+push; Critical→+SMS/WhatsApp, pierces quiet hours | V04 B4 | P1 |
| BR-NTF-02 | Per-user preferences (event-category × channel), quiet hours 22:00–07:00 (Critical pierces), digests | V04 B7 | P1 |
| BR-NTF-03 | Throttling: per-rule caps + coalescing digests + storm breaker (flood → auto-digest + admin alert) | V04 B4 | P2 |
| BR-NTF-04 | Templates versioned (en/bn), tenant overrides, validated against event schema at save | V04 B5 | P1 |
| BR-NTF-05 | At-least-once with idempotency key (event × recipient × channel); retries + DLQ; provider failover | V04 B8 | P1 |
| BR-NTF-06 | Mandatory rules (security, Critical finance) cannot be muted by users | V04 B7 | P1 |
| BR-NTF-07 | Full delivery log with provider receipts; analytics (delivery/read rate, cost per tenant) | V04 B8 | P1 |
| BR-NTF-08 | Recipient resolves to zero users → admin alert, never silent drop | V04 B9 | P1 |
| BR-NTF-09 | Non-suppressible business alerts: LC maturity T-7/3/1, demurrage 70%, SLA breach, assessment variance (to responsible positions) | v1 doc 02 §8 | P1 |

---

## 2. Vendor Management (BR-VEN)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-VEN-01 | Lifecycle: Prospect → Registered → Under Qualification → Qualified → Active → On Hold → Blacklisted → Archived | v1 VR-01 | P1 |
| BR-VEN-02 | Only Active vendors selectable on RFQ/PO; override = Procurement Head approval | v1 VR-01 | P1 |
| BR-VEN-03 | Duplicate detection on (TIN | BIN | bank account | normalized name+country); merge preserves history | v1 VR-02 | P1 |
| BR-VEN-04 | Mandatory KYC by type — local: trade license, TIN, BIN/VAT reg, bank proof; foreign: registration cert, bank (SWIFT); beneficiary name must match LC docs later (hard validation at LC application) | v1 VR-03 | P1 |
| BR-VEN-05 | Bank account add/change = maker-checker + auto-notification to vendor's registered email | v1 VR-04 | P1 |
| BR-VEN-06 | Qualification per category; expiry & re-qualification cycle (default 24 months; auto-spawn 60 days before expiry) | v1 VR-05 | P1 |
| BR-VEN-07 | Blacklisting requires reason code + approver; blocks new documents, not in-flight settlements | v1 VR-06 | P1 |
| BR-VEN-08 | Scorecard monthly: OTD 35%, Quality 30%, Price 15%, Responsiveness 10%, Compliance 10%; grades A ≥ 85, B ≥ 70, C ≥ 55, D < 55; two consecutive D → auto On-Hold proposal | v1 VR-07 | P1 |
| BR-VEN-09 | Every closed PO contributes delivery/quality/responsiveness datapoints automatically | v1 doc 01 §3 | P1 |

## 3. Sourcing / RFQ (BR-SRC)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-SRC-01 | Minimum-bidder policy by amount (default 3 bidders > BDT 5L); waivers need Procurement Head approval + justification code (urgency / proprietary / OEM) | v1 RFQ-01 | P1 |
| BR-SRC-02 | Invited vendors must be Qualified for the category (override path exists) | v1 RFQ-02 | P1 |
| BR-SRC-03 | Sealed mode hides bids until deadline; late bids flagged, acceptance needs Sourcing Mgr approval | v1 RFQ-01 | P1 |
| BR-SRC-04 | RFP weighted envelopes (default 60/40 technical/commercial); committee quorum; commercial opened only for technically qualified bidders | v1 RFQ-03 | P2 |
| BR-SRC-05 | Comparison normalizes to **landed-cost basis** for imports: bid + Incoterm-aware freight + HS-based duty + handling | v1 RFQ-04 | P1 |
| BR-SRC-06 | Award ≠ lowest landed TCO requires justification + CFO co-approval | v1 RFQ-05 | P1 |
| BR-SRC-07 | Full bid tab snapshot frozen at award (immutable) | v1 RFQ-06 | P1 |
| BR-SRC-08 | Award must reference the bid analysis record; sourcing strategy mandatory above tenant threshold (default BDT 50L/yr) | v1 doc 01 §3 | P1 |
| BR-SRC-09 | PO auto-drafted from award (split % honored) | v1 3.3 journey | P1 |

## 4. Contracts & Blanket PO (BR-CNT)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-CNT-01 | Blanket PO: validity window + value/qty caps; call-offs consume cap; expiry alerts | v1 3.5 | P1 |
| BR-CNT-02 | PO under a valid rate agreement inherits price/terms; off-contract purchase of contracted item → maverick-spend flag | v1 doc 01 §3 / 3.5 | P1 flag; P2 contract mgmt |
| BR-CNT-03 | Full contracts: price lists per item with stepped/volume pricing; escalation rules; renewal clones with redline diff | v1 3.5 | P2 |
| BR-CNT-04 | Service contracts carry deliverable milestones/SLAs | v1 3.5 | P2 |
| BR-CNT-05 | Contract expiry alerts T-60/T-30 | v1 3.5 | P2 |

## 5. Purchase Requisition (BR-PR)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-PR-01 | Line-level need-by ≥ today + category lead time (warning) | v1 PR-01 | P1 |
| BR-PR-02 | Budget reservation at submit (soft-block; hard-block configurable per category) | v1 PR-02 | P1 |
| BR-PR-03 | Item or free-text lines; free-text requires category for budget mapping | v1 PR-03 | P1 |
| BR-PR-04 | Buyers can merge PR lines into one sourcing case / PO | v1 PR-04 | P1 |
| BR-PR-05 | Cancellation releases reservations | v1 PR-05 | P1 |
| BR-PR-06 | PR approval per DoA slab; budget-fail branch to Budget Owner | v1 3.2 | P1 |

## 6. Purchase Order (BR-PO)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-PO-01 | Sources: from award, contract call-off, PR direct (within DoA), manual (lowest privileges) | v1 PO-01 | P1 |
| BR-PO-02 | Import PO requires: foreign vendor, Incoterm, currency, HS code per line, port pair, payment mode (LC/TT/contract) | v1 PO-02 | P1 |
| BR-PO-03 | **Feasibility gate:** on submit, engine returns score 0–100 + breakdown; below tenant threshold (default 60) → blocked pending CFO override with reason; snapshot stored on PO (immutable evidence) | v1 PO-03 | P1 |
| BR-PO-04 | Revisions version the PO (R0, R1…); value-increasing revisions re-enter approval; dispatched revisions notify vendor + require acknowledgement | v1 PO-04 | P1 |
| BR-PO-05 | Budget moves reservation→commitment at approval | v1 PO-05 | P1 |
| BR-PO-06 | Auto-close when received ≥ ordered − tolerance and all invoices matched; force-close requires reason + releases residual commitment | v1 PO-06 | P1 |
| BR-PO-07 | LC terms must not contradict PO terms — validated at LC application (currency, amount ± tolerance, latest shipment, ports, partial-shipment flag) | v1 IPO-01 | P1 |
| BR-PO-08 | Import PO adds: shipment tolerance (±% qty, partial/transshipment flags mirroring LC), latest shipment date, PSI requirement, link to Import File | v1 4.2 | P1 |

## 7. Invoice & 3-Way Match (BR-INV)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-INV-01 | Capture channels: manual, supplier portal, OCR-assist; all carry source + confidence | v2 V01 / V02 §3.8 | P1 |
| BR-INV-02 | Line-level 3-way match PO↔GRN↔Invoice; default tolerances: price ±2%, qty −0%/+1% (0 for batch-controlled), tax exact | v1 doc 01 §4 | P1 |
| BR-INV-03 | Out-of-tolerance → exception queue with reason codes (price/qty/tax variance, missing GRN); > tolerance → Finance Head | v1 doc 01 §4 / 3.6 | P1 |
| BR-INV-04 | Duplicate check on (vendor, invoice_no, amount) | v1 3.6 | P1 |
| BR-INV-05 | Import vendor invoices reconcile against CI | v1 3.6 | P1 |
| BR-INV-06 | Service POs: 2-way match with completion certificate | v1 3.6 | P1 |
| BR-INV-07 | Credit notes: maker-checker | v1 3.6 | P1 |
| BR-INV-08 | Auto-close PO when fully received + matched (ties to BR-PO-06) | v1 doc 01 §4 | P1 |

## 8. Budget (BR-BUD)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-BUD-01 | Budgets per FY × cost center × category (project optional) | v1 3.8 | P1 |
| BR-BUD-02 | Lifecycle: reserve (PR) → commit (PO) → consume (GRN/Invoice per tenant policy) | v1 3.8 | P1 |
| BR-BUD-03 | Revisions versioned; Budget Owner → CFO approval | v1 3.8 | P1 |
| BR-BUD-04 | Hard/soft block configurable per category | v1 PR-02 | P1 |
| BR-BUD-05 | Append-only budget transaction ledger (type, source doc, amount, balance_after) | v1 3.8 | P1 |

## 9. Goods Receipt & QC (BR-GRN)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-GRN-01 | GRN references PO (+ shipment/CI for imports); over-receipt within tolerance else approval | v1 GR-01 | P1 |
| BR-GRN-02 | Stages: Gate Entry → Unloading tally → QC → Accept/Reject/Deviation-accept → Putaway | v1 GR-02 | P1 |
| BR-GRN-03 | Shortage/damage on imports recorded with survey evidence → insurance-claim draft; cost treatment per BR-LCS-04 | v1 GR-03 | P1 |
| BR-GRN-04 | GRN posts GR/IR accrual at provisional cost (PO price or forecast landed); revalued at finalization | v1 GR-04 | P1 |
| BR-GRN-05 | Vendor metric events emitted (OTD vs need-by, acceptance rate) | v1 GR-05 | P1 |
| BR-GRN-06 | Rejected qty auto-creates return/debit-note draft | v1 3.7 | P1 |
| BR-GRN-07 | GRN only after release order (imports) | V01 IMP-05 | P1 |
| BR-GRN-08 | QC decision by QC Inspector; deviation-accept → Store Mgr + QC Head | v1 8.1 | P1 |

## 10. Import File (BR-IMP / BR-IF)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-IMP-01 | No LC application without valid IRC + approved PI (+ accepted per v1: insurance cover note reference, HS-classified items, permit check passed) | V01 IMP-01 / v1 IMP-01 | P1 |
| BR-IMP-02 | Insurance before LC on CFR/FOB terms | V01 IMP-02 | P1 |
| BR-IMP-03 | BoE line items must map to CI lines; qty/value mismatches logged as assessment variances | V01 IMP-03 / v1 IMP-03 | P1 |
| BR-IMP-04 | Duty paid only against assessed BoE | V01 IMP-04 | P1 |
| BR-IMP-05 | GRN only after release order | V01 IMP-05 | P1 |
| BR-IMP-06 | Cost sheet cannot finalize with clearing balance ≠ 0; file cannot close until all cost elements invoiced or accrued | V01 IMP-06 / v1 IMP-06 | P1 |
| BR-IMP-07 | AIT/AT posted as recoverable (advance tax) assets unless tenant policy elects expensing; per-consignment override by Finance | V01 IMP-07 / v1 IMP-07 | P1 |
| BR-IMP-08 | File cannot close with unmatched IMP form or missing mandatory documents | V01 IMP-08 | P1 |
| BR-IF-01 | State machine: 15 states + Held/Disputed/Cancelled (BRS-Core §5.3); transitions validated | v1 doc 04 | P1 |
| BR-IF-02 | Demurrage clock starts at port-defined free days from landing; alert at 70% of free time | v1 IMP-05 | P1 |
| BR-IF-03 | Per-stage SLA (tenant-configurable); breaches notify owners + aging widget | v1 doc 01 §7 | P1 |
| BR-IF-04 | File number `IMP-{COMPANY}-{FY}-{SEQ}`; one active C&F agent per file per port; reassignment logs custody transfer | v1 doc 04 / CF-01 | P1 |

## 11. Import Documents (BR-DOC)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-DOC-01 | PI lines must reconcile to PO lines (qty/price tolerance check) | v1 PI-01 | P1 |
| BR-DOC-02 | PI acceptance freezes the version used for LC; later PIs become amendments | v1 PI-02 | P1 |
| BR-DOC-03 | PI validity tracked; expiry alert T-7 | v1 PI-03 / 4.3 | P1 |
| BR-DOC-04 | PI bank/beneficiary details must match vendor master (fraud gate; hard validation at LC) | v1 PI-04 | P1 |
| BR-DOC-05 | CI reconciles to PI/PO and later to BoE | v1 CI-01 | P1 |
| BR-DOC-06 | PL cartons/weights must total CI quantities; net/gross weight + CBM per line (drives weight/volume allocation) | v1 CI-02 | P1 |
| BR-DOC-07 | Discrepancy log vs LC terms (basis for bank discrepancy handling) | v1 CI-03 | P1 |
| BR-DOC-08 | Multiple CIs per file supported (partial shipments); each spawns a Shipment | v1 CI-04 | P1 |
| BR-DOC-09 | BL/AWB: MBL/HBL + MAWB/HAWB with on-board date (drives LC latest-shipment compliance), freight terms, consignee/notify must match LC, original count / surrender status | v1 BL-01 | P1 |
| BR-DOC-10 | Bank endorsement status tracked (BL released by bank → C&F can file BoE); custody chain supplier→bank→importer→C&F→customs | v1 BL-02 | P1 |
| BR-DOC-11 | BL amendments logged with charge capture | v1 BL-03 | P1 |

## 12. HS Codes, Permits & COO (BR-HS / BR-PM)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-HS-01 | BD 8-digit tariff lines preloaded from NBR schedule, effective-dated per Finance Act | v1 HS-01 | P1 |
| BR-HS-02 | Item↔HS mapping at item master with confidence + approved-by | v1 HS-02 | P1 |
| BR-HS-03 | Per-consignment HS override allowed (logged; variance vs assessment tracked) | v1 HS-03 | P1 |
| BR-HS-04 | Duty rates live on dated `duty_rate` rows keyed (hs_code, component, effective_from) | v1 HS-04 | P1 |
| BR-HS-05 | HS mapping changes: maker (Tax Officer) → checker (Finance Head) | v1 4.6 | P1 |
| BR-PM-01 | File cannot advance to LC stage if its category requires a permit that is missing/expired | v1 PM-01 | P1 |
| BR-PM-02 | Permit utilization ledger (qty/value drawn per consignment) + IRC ceiling tracking | v1 PM-02 | P1 |
| BR-PM-03 | COO capture (generic / SAFTA / SAPTA / APTA / China-BD FTA) with preferential-rate eligibility feeding the duty engine | v1 4.8 | P1 |
| BR-PM-04 | COO vs CI origin mismatch → alert | v1 4.8 | P1 |

## 13. Shipment, Container, Freight (BR-SHP / BR-FRT)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-SHP-01 | Shipment created per CI (or planned pre-CI) | v1 SH-01 | P1 |
| BR-SHP-02 | Milestone chain by mode — Sea: booking→stuffing→gate-in→ETD/ATD→transshipment(s)→ETA/ATA→discharge→IGM; Air: booking→ATD→ATA | v1 SH-02 | P1 |
| BR-SHP-03 | ETA changes versioned; slippage > x days notifies planner + feeds delay-prediction training data | v1 SH-03 | P1 |
| BR-SHP-04 | LC latest-shipment-date breach risk alert at T-7 | v1 SH-04 | P1 |
| BR-SHP-05 | Container no ISO 6346 check-digit validated; event stream tracked; demurrage/detention clocks per container (port vs line free days) with cost projection | v1 4.12 | P1 |
| BR-SHP-06 | Carrier API / aggregator webhook ingestion | v1 4.12 | P3 |
| BR-FRT-01 | Estimated freight auto-suggested from lane history (Cost Forecasting Engine) | v1 FR-01 | P1 |
| BR-FRT-02 | Forwarder bills reconciled line-by-line vs quote; variances need Import Mgr approval | v1 FR-02 | P1 |

## 14. Insurance (BR-INS)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-INS-01 | Marine cover note before LC (BD requirement); insured value = CI × 110% default | v1 4.15 | P1 |
| BR-INS-02 | Open cover: master policy + per-shipment declarations + aggregate utilization | v1 4.15 | P2 |
| BR-INS-03 | Claims sub-ledger (survey → lodged → settled) | v1 4.15 | P1 |
| BR-INS-04 | Premium (+VAT) becomes landed-cost element | v1 4.15 | P1 |

## 15. C&F Agents (BR-CNF)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-CNF-01 | Agent master (AIN license, ports, rate card: per-BoE / per-container / % of value + documentation charges); portal access to assigned files only | v1 4.19 | P1 |
| BR-CNF-02 | Charge-bill capture with rate-card auto-verification; variances flagged | v1 4.19 | P1 |
| BR-CNF-03 | Performance scorecard (clearance days, query rate, demurrage incidence, billing accuracy) | v1 4.19 | P2 |

## 16. Letters of Credit (BR-LC)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-LC-01 | Prerequisites: accepted PI, insurance cover note + money receipt, valid IRC with ceiling, LCA data complete, HS classification done, permit check passed | v1 LC-01 | P1 |
| BR-LC-02 | Types: sight, usance (30/60/90/120/180/360), UPAS; transferable/back-to-back flags | v1 LC-02 | P1 |
| BR-LC-03 | Term consistency vs PO/PI: amount ± tolerance, currency, latest shipment, expiry (≥ latest shipment + 21 days default), ports, partial/transshipment, Incoterm, documents list | v1 LC-03 | P1 |
| BR-LC-04 | Margin % per bank arrangement at opening; blocked → restricted funds; released/adjusted at retirement; top-ups on amendments | v1 LC-04 | P1 |
| BR-LC-05 | Facility exposure ledger per bank: limit, outstanding (open LCs + accepted bills + loans), available; application blocked if insufficient (override: CFO) | v1 LC-05 | P1 |
| BR-LC-06 | Presentations: discrepancy notices logged (code list), accept/refuse workflow; acceptance creates maturity obligation (bill) per tenor | v1 LC-06 | P1 |
| BR-LC-07 | Retirement: own funds / import loan / partial; FX deal rate captured → realized FX vs booking rate | v1 LC-07 | P1 |
| BR-LC-08 | All bank charges (opening, amendment, acceptance, SWIFT, confirmation, handling) captured per event → file cost ledger | v1 LC-08 | P1 |
| BR-LC-09 | Watchdogs: expiry/shipment alerts T-15/T-7/T-3; expired-undrawn LC closure releases margin & facility | v1 LC-09 | P1 |
| BR-LC-10 | Amendments: value/tenor-increasing → CFO; clerical → Import Mgr; reason taxonomy feeds analytics | v1 5.1 / 5.8 | P1 |

## 17. BTB, TT, Loans, Margin, Obligations (BR-BTB / BR-TT / BR-LON / BR-MRG / BR-OBL)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-BTB-01 | B2B LCs open against master export LC within entitlement (default ≤ 75% of export LC value for woven; tenant-configurable; knit per net-FOB rules) | v1 B2B-01 | P2 |
| BR-BTB-02 | Maturity alignment: B2B usance maturity should fall after expected export realization; misalignment warning | v1 B2B-02 | P2 |
| BR-BTB-03 | Utilization ledger master ↔ B2B LCs + realization matching | v1 B2B-03 | P2 |
| BR-TT-01 | Advance/partial/balance schedule within regulatory advance limits; beyond policy → warning + CFO approval | v1 TT-01 | P1 |
| BR-TT-02 | TT requests generate bank instruction pack; beneficiary from vendor master — immutable match rule | v1 5.4 / doc 11 H | P1 |
| BR-TT-03 | TT status: requested → executed (value date, FX rate, charges) → shipment-matched | v1 5.4 | P1 |
| BR-TT-04 | SWIFT registry (MT103/202, MT700/707/750/754/756) linked to LC/TT; unmatched-reference inbox | v1 5.5 | P1 |
| BR-TT-05 | Import contracts (bank registration): utilization vs remittances; unmatched remittance aging alerts (IMP matching) | v1 5.3 | P1 |
| BR-LON-01 | Import loans (LTR / MPI / time loan) created at LC retirement or post-clearance | v1 5.6 | P2 |
| BR-LON-02 | Interest/profit accrual daily (simple/compound per bank); repayment entries reduce outstanding; alerts T-5/overdue/rate-reset | v1 5.6 | P2 |
| BR-LON-03 | Interest cost policy: expense (default) or landed cost for long tenor — tenant accounting policy | v1 5.6 | P2 |
| BR-MRG-01 | Cross-instrument restricted-cash view: margin events + guarantee margins aggregated by bank/company; daily visibility | v1 5.7 | P1 |
| BR-MRG-02 | Margin aging + opportunity-cost estimate widget | v1 5.7 | P2 |
| BR-OBL-01 | Unified obligation calendar: LC maturities, TT schedule lines, loan installments, vendor invoice due dates | v1 5.9 | P1 |
| BR-OBL-02 | Cash-requirement forecast by week/currency; xlsx export for treasury | v1 5.9 | P1 |
| BR-OBL-03 | Per-obligation notifications T-7/T-3; weekly treasury digest | v1 5.9 | P1 |

## 18. Customs & Duty (BR-CUS / BR-DS)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-DS-01 | Rates effective-dated (hs_code, component, rate, specific_rate, uom, effective_from/to, source: finance_act/sro/manual, ref_doc); **no overlapping periods** (DB exclusion constraint) | v1 DS-01 | P1 |
| BR-DS-02 | Maker-checker on all duty-rate changes | v1 DS-02 | P1 |
| BR-DS-03 | Annual Finance Act import wizard (xlsx diff, mass effective-dating July 1) | v1 DS-03 | P2 |
| BR-DS-04 | Every calculation stores the rate-row ids used (reproducibility — recalculate any historical consignment exactly) | v1 DS-04 | P1 |
| BR-DS-05 | SRO layer: registry + benefits (exempt / rate_override / cap + conditions e.g., industrial IRC, bonded, capital machinery) resolved by HS + tenant eligibility; itemized on breakdown | v1 6.1 | P1 registry; P2 automation |
| BR-CUS-01 | Clearance checklist auto-generated per file (mode/port/category aware) | v1 CC-01 | P1 |
| BR-CUS-02 | BoE mirrors ASYCUDA fields (no/date, office, declarant AIN, AV per line, duty lines); status: submitted → queried → assessed → paid → examined (G/Y/R) → released | v1 CC-02 | P1 |
| BR-CUS-03 | System-computed vs assessed duty variance per line; > tolerance → dispute record (query response / appeal / provisional release under bank guarantee — tracked in guarantee register) | v1 CC-03 / IMP-04 | P1 |
| BR-CUS-04 | Port stage: delivery order, port dues/charges capture, examination findings, gate-out; demurrage accrual auto-computed daily after free time | v1 CC-04 | P1 |
| BR-CUS-05 | Every clearance milestone timestamped for SLA analytics (BoE→assessment→payment→release) | v1 CC-05 | P1 |
| BR-CUS-06 | Challan register with scanned evidence; Σ challans per file = assessed TTI; unpaid-assessment aging is a release blocker view | v1 6.5 | P1 |
| BR-CUS-07 | AIT/AT advance-tax sub-ledgers per company per FY (additions per consignment, adjustments per return period, closing balance) | v1 6.3 | P1 |
| BR-CUS-08 | Assessed vs paid should be zero — variance is an exception | v1 6.3 | P1 |

## 19. Landed Cost (BR-LCS)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-LCS-01 | Cost sheet auto-created with the import file | v1 LC-SHEET-01 | P1 |
| BR-LCS-02 | Estimates seeded from Feasibility/Forecast engines at draft | v1 LCS-02 | P1 |
| BR-LCS-03 | Lifecycle: Draft → Accumulating → Ready → Finalized → Adjusted; finalize blocked until completeness (Incoterm-aware expectation list); accrual entries allowed for missing bills (flagged, reversed on actual) | v1 6.6 / LCS-03 | P1 |
| BR-LCS-04 | Received qty (GRN) is the division basis; shortage/damage cost to expense or spread — tenant policy | v1 LCS-04 | P1 |
| BR-LCS-05 | Every figure traceable to source document id | v1 LCS-05 | P1 |
| BR-LCS-06 | Allocation: drivers = value, qty, net_weight, gross_weight, volume(CBM), container_share, direct, manual %; element scope = file / shipment / selected lines; tenant defaults per element; per-sheet override with reason | v1 6.6 | P1 |
| BR-LCS-07 | Rounding: banker's rounding at 4 dp unit cost; residual pennies to largest line (deterministic); each element converted at its own document FX rate; FCY detail + BDT totals | v1 6.6 | P1 |
| BR-LCS-08 | Finalization: Import Mgr submits → Finance Head approves → posts InventoryCostUpdated + GL journals; variance report vs feasibility snapshot auto-generated | v1 6.6/6.7 | P1 |
| BR-LCS-09 | Post-finalization late bills → adjustment sheet version, delta postings | v1 6.6 | P1 |
| BR-LCS-10 | Cost vs. recoverable treatment per component: CD/RD/SD always landed cost; VAT recoverable if registered claimant (else cost); AIT/AT advance assets by default — engine outputs both subtotals per line | v1 6.1 | P1 |

## 20. Inventory Valuation (BR-VAL)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-VAL-01 | Weighted average default; FIFO layers optional per tenant/item-class | v1 8.3 | P1 WA; P2 FIFO |
| BR-VAL-02 | Receipt at provisional cost; on LandedCostFinalized → revalue on-hand; consumed portion → COGS adjustment line (policy: adjust COGS vs variance account) | v1 8.3 | P1 |
| BR-VAL-03 | Append-only inventory value ledger (item, site, txn_type, qty, unit_cost, value_delta, source_doc) | v1 8.3 | P1 |
| BR-VAL-04 | Manual cost adjustments: maker-checker + reason taxonomy; period-close lock (late postings flagged prior-period) | v1 8.3 | P1 |
| BR-VAL-05 | Imports: default batch = import file no (provenance batch→file→BoE→supplier; recall-ready); expiry-aware (FEFO suggestion via APIs) | v1 8.2 | P1 |

## 21. Finance / GL / AP (BR-FIN)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-FIN-01 | Event → journal map tenant-remappable (defaults in BRS-Core §5.4 + v1 8.5 table); posting engine + export adapters (API push, Tally XML, xlsx) | v1 8.5 | P1 engine + xlsx; P2 adapters |
| BR-FIN-02 | Clearing-account zero-balance check per file at closure; residue blocks closure → reconciliation task | v1 8.5 | P1 |
| BR-FIN-03 | Every PR/PO line carries cost center (dept × site default, project optional); landed-cost postings inherit; overhead charge bills allocatable by % matrix | v1 8.6 | P1 |
| BR-FIN-04 | Period-end accrual automation: GRN-not-invoiced → reversing accrual journal; optional in-transit expected-cost accruals | v1 8.8 | P2 |
| BR-FIN-05 | AP: aging, due-date engine, payment proposal runs, instruction file export (bank formats), settlement entry, vendor advice email; no direct bank execution | v1 8.4 | P1 |
| BR-FIN-06 | FX: dated, source-tagged rates (Bangladesh Bank / bank deal rate); realized FX on settlement; unrealized policy | v1 doc 01 §7 | P1 |

## 22. Intelligence Engines (BR-AI)

| ID | Rule | Source | Phase |
|---|---|---|---|
| BR-AI-01 | Feasibility runs automatically at import-PO submit; < 3 s; snapshot (score, factor table, P50/P80 landed cost per line, expected margin, timeline, top-3 risks, counterfactual hints) stored immutably on the PO | v1 7.3 / PO-03 | P1 |
| BR-AI-02 | Scoring: tenant-tunable weights, defaults — Margin adequacy 30, Cost competitiveness 20, Supplier risk 20, Timeline fit 15, Historical variance 10, Plan & budget alignment 5; factors normalized 0–100 via tenant-calibrated breakpoints; threshold default 60 | v1 7.3 | P1 |
| BR-AI-03 | Below threshold → CFO override path with reason code; quarterly calibration report (score deciles vs realized margin/delay) | v1 7.3 | P1 |
| BR-AI-04 | Cost forecasting v1 (statistical): freight by lane median (6-mo tenant + opt-in anonymized pool), duty deterministic from tables (+Finance-Act-window uncertainty flag), port/C&F medians, bank schedule + margin opportunity cost, FX drift proxy, transit median | v1 7.2 | P1 |
| BR-AI-05 | Low-sample elements (< 5 observations) fall back to tenant defaults, flagged low-confidence; outputs P50/P80 + basis + sample_n + staleness | v1 7.2 | P1 |
| BR-AI-06 | Supplier risk v1: 5 pillars — Performance 35, Financial/Commercial 20, Compliance 20, Concentration 15, External 10; decay-weighted; event-driven + nightly recompute; watch rules (step > 15 pts, grade crossing, sanctions hit → alerts; extreme → auto On-Hold proposal) | v1 7.4 | P1 |
| BR-AI-07 | Duty calculator: deterministic, rate-lineage-stamped (BR-DS-04); exposed in-process, via API (what-if), and batch (Finance-Act impact) | v1 7.1 | P1 |
| BR-AI-08 | Engine invariants property-tested: rate↑ ⇒ TTI↑; Σ allocated = element amount; rounding residual ≤ 1 unit | v1 7.1 | P1 |
| BR-AI-09 | Every engine output = persisted, versioned snapshot with full input lineage (audit reproducibility) | v1 07 intro | P1 |
| BR-AI-10 | ML layer (Enterprise): freight/dwell/uplift models, delay classifier, recommenders, survival model — served via sidecar with statistical fallback on timeout/drift | v1 7.2–7.7 | P3 |
| BR-AI-11 | OCR document processing: bilingual, tuned for BD trade paper (PI/CI/PL/BL/BoE), auto-reconciliation suggestions; every AI-extracted field carries source + confidence; human confirmation on financial fields | v2 V00 §1 / V02 §3.8 | P1 |
| BR-AI-12 | LLM calls only via internal AI gateway (prompt/PII policy enforced); tenant data never used for cross-tenant training without opt-in | V02 §5 | P1 |

---

## 23. Deterministic Computation Reference

### 23.1 Assessable value & duty cascade (per BoE line, dated rates)
```
CIF = FOB + Freight + Insurance                      (foreign currency)
AV  = CIF × customs_exchange_rate × (1 + landing_charge_pct)   -- landing charge default 1%
Customs FX rate = NBR-notified monthly rate (distinct from bank booking rate — both stored)

CD  = AV × cd_rate
RD  = AV × rd_rate
SD  = (AV + CD + RD) × sd_rate
VAT = (AV + CD + RD + SD) × vat_rate                 -- usually 15%
AIT = AV × ait_rate                                  -- typ. 5%
AT  = (AV + CD + RD + SD) × at_rate                  -- typ. 5%
TTI = CD + RD + SD + VAT + AIT + AT

Specific duties: any component = max(ad_valorem, qty × specific_rate)
Tariff-value floor: AV_effective = max(declared_AV, tariff_value) with flag
SRO layer resolves by HS + tenant eligibility → exempt / rate_override / cap, itemized on breakdown
```

### 23.2 Landed-cost allocation (per item line)
```
allocated(element, line) = element_amount × driver_value(line) / Σ driver_value(lines in element scope)
unit_landed_cost(line)   = [ goods_value + Σ allocated(element, line) ] / received_qty(line)
```
Scope = whole file / one shipment / selected lines (e.g., demurrage of one container → only lines stuffed in it). Rounding: banker's at 4 dp; residual pennies to largest line. Currency: each element at its own document FX rate; sheet reports FCY detail + BDT totals.

### 23.3 Feasibility score
```
margin_pct        = (selling_or_standard_price − expected_landed_unit) / selling_or_standard_price
timeline_days     = production_lead + transit_p50 + clearance_p50 + inland
score             = Σ weighted factors (defaults in BR-AI-02), each normalized 0–100
decision          = score ≥ tenant.threshold (default 60) ? proceed : CFO-override path
```

### 23.4 Default tolerances (tenant-configurable)
| Tolerance | Default |
|---|---|
| Invoice price variance | ±2% |
| Invoice qty variance | −0% / +1% (0 for batch-controlled items) |
| Tax variance | exact |
| PO→PI qty/price | tenant-defined (BR-DOC-01) |
| LC amount vs PO | LC tolerance_pct per LC (BR-LC-03) |
| Assessment variance (computed vs assessed) | tenant-defined; > tolerance → dispute (BR-CUS-03) |
| Over-receipt | tenant-defined per item (BR-GRN-01) |
| Post-approval amount growth re-approval | > 5% (DoA §4.4) |
| RFQ minimum bidders | 3 above BDT 5L |
| Sourcing-strategy threshold | BDT 50L/yr category spend |
