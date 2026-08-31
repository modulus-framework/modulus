# V01 — Business Requirement Specification (BRS)
Covers master topic: 01

---

## 1. Business Context & Problem Statement

South Asian importing enterprises run four disconnected worlds: (a) **procurement** in email/Excel with no sourcing discipline; (b) **import operations** in paper files owned by one irreplaceable "import officer"; (c) **trade finance** visible only inside bank portals; (d) **costing** reconstructed months later, wrongly, in spreadsheets. Consequences, validated with design partners:

| Pain | Evidence pattern | Business cost |
|---|---|---|
| Unknown true landed cost at decision time | Duty/freight "estimated" as flat %; SRO benefits missed | 3–8% margin leakage; mispriced sales quotes |
| Maverick & duplicate buying | No PR discipline, no contract compliance checks | 5–12% addressable spend leakage |
| LC/loan maturity surprises | Maturities tracked in one person's diary | Penal interest, forced forex at bad rates, facility breaches |
| Demurrage & detention | No container clock ownership | BDT 2–20 lakh/yr per mid-size importer |
| Assessment disputes lost | No declared-vs-assessed history, no rate lineage | Overpaid duty never recovered |
| Vendor risk invisible | No scorecards; single-source dependencies unknown | Stockouts, quality failures, compliance exposure |
| Audit pain (NBR/BB/buyers) | Documents scattered; no immutable trail | Penalties; failed buyer audits (RMG) |
| Key-person risk | Import knowledge in heads, not systems | Operations stall on resignation |

## 2. Business Goals → Requirement Traceability

| Goal | Measure | Primary BR clusters |
|---|---|---|
| G1 Cost truth | 100% of import receipts carry finalized landed cost within 10 days of release | BR-LC-*, BR-IMP-*, BR-CST-* |
| G2 Spend control | ≥ 95% of spend via approved PO; contract compliance ≥ 85% | BR-PR-*, BR-PO-*, BR-CNT-* |
| G3 Finance discipline | Zero missed LC/loan maturities; margin cash visible daily | BR-TF-* |
| G4 Cycle time | PR→PO ≤ 3 days (indirect), import file ETD→release ≤ dwell benchmark −20% | BR-WF-*, BR-SHP-* |
| G5 Compliance | Every BoE reproducible (rate lineage); IMP forms matched ≥ 98% | BR-CUS-*, BR-BB-* |
| G6 Intelligence | Feasibility score on 100% of import POs pre-approval | BR-AI-* |
| G7 Supplier value | Top-80%-spend vendors scored quarterly; risk alerts < 24h | BR-VEN-* |

## 3. Actors (canonical, referenced by all volumes)

**Buy-side:** Requester, Buyer/Procurement Officer, Procurement Head, Category Manager, Sourcing Specialist, Contract Manager, Import Executive, Import Manager, Commercial/Trade-Finance Officer, Finance Manager, CFO, Cost Accountant, AP Clerk, Store/Warehouse Keeper, QC Inspector, Budget Owner, Internal Auditor, Compliance Officer, Tenant Admin, Group Controller.
**Sell-side/external:** Supplier (local), Foreign Supplier, C&F Agent, Freight Forwarder, Insurance Provider, Bank (Trade Desk), Surveyor/Inspection Agency.
**System actors:** Workflow Engine, Notification Engine, AI Engines, Scheduler, Integration Hub.

## 4. End-to-End Lifecycles

### 4.1 Source-to-Contract (S2C)
Demand signal (plan/reorder/project BOM) → category strategy → supplier discovery/qualification → RFI (capability screen) → RFQ/RFP (or reverse auction) → landed-cost-normalized comparison → negotiation → award → contract/framework agreement with rate cards, SLAs, penalties → contract activation → compliance monitoring → renewal/exit.
**Key rules:** sealed bids until deadline; technical/commercial envelope separation for RFP; award splits allowed with justification; every award traceable to comparison snapshot; contract prices become PO price source with tolerance-gated overrides.

### 4.2 Procure-to-Pay (P2P) / Request-to-Pay (R2P)
PR (catalog / free-text / punch-in from plan) → budget check (reserve) → PR approval per DoA → sourcing path decision (contract call-off | RFQ | direct within threshold) → PO (budget commit) → supplier acknowledgement → (expedite) → GRN + QC → invoice capture (portal/OCR/manual) → **3-way match** (price ±2%, qty −0%/+1% default, configurable per tenant/category) → exception workflow → AP posting instruction → payment status feedback → budget consume → closure.
**R2P** = P2P entered from employee request UX with guided catalogs and auto-sourcing; identical spine.
**Edge cases handled:** partial receipts/invoices, over-shipment tolerance, returns/debit notes, price retro-adjustments from contract amendments, closed-period invoice arrival, PO cancellation with committed budget release, advance payments with adjustment tracking.

### 4.3 Import-to-Inventory (Bangladesh canonical flow)
1. Import plan / import PO (IPO) approved with **feasibility snapshot**.
2. **PI** received → validated vs IPO (price/qty/Incoterm/HS) → PI approval.
3. **Insurance cover note** (CIF builds: CI×110% default; open-cover utilization check).
4. **LCA form + IRC validation** → **LC application** → margin computed & blocked → LC issued (MT700 logged) → amendments as needed.
5. Supplier ships → **shipment** record: ETD/ETA, vessel/voyage or flight, containers assigned, milestones tracked; **BL/AWB** captured, custody chain (issued→endorsed→surrendered).
6. Documents presented → bank scrutiny → discrepancy handling → acceptance (usance: maturity created) → **bank endorsement** of documents.
7. C&F nominated → pre-alert docs → **BoE filed in ASYCUDA** (system mirrors entry) → assessment (lane G/Y/R) → declared-vs-assessed variance workflow → **duty payment** (challan) → examination → release order → port/ICD gate-out (demurrage/detention clocks close).
8. Inland transport → **GRN** (batch = import file no) → QC → putaway.
9. **Cost sheet** accumulates estimated→committed→actual per element → all actuals in → finalize → **inventory revaluation + GL postings** → import file closed after IMP matching & document archive complete.
**Critical rules (BR-IMP):** IMP-01 no LC without valid IRC + PI approval; IMP-02 insurance before LC (CFR/FOB terms); IMP-03 BoE lines must map to CI lines (qty/HS reconciliation); IMP-04 duty paid only against assessed BoE; IMP-05 GRN only after release order; IMP-06 cost sheet cannot finalize with clearing balance ≠ 0; IMP-07 AIT/AT posted as recoverable assets unless tenant policy elects expensing; IMP-08 file cannot close with unmatched IMP form or missing mandatory documents.

### 4.4 Import-to-Finance
Every import event has a finance shadow: LC margin (restricted cash) → acceptance (liability recognition) → duty challan (tax assets/expense split) → GR/IR clearing → landed cost capitalization → LC retirement/loan creation (LTR/MPI) → realized FX on settlement → IMP reporting to Bangladesh Bank. Requirement: **any BDT amount in GL traces to a source document line in ≤ 3 clicks.**

### 4.5 Supplier Collaboration Lifecycle
Invitation → registration (self-service) → qualification (docs, references, financials, site audit for strategic) → approved-with-scope (categories/sites) → transacting (RFQ responses, PO ack, ASN, invoice submission, scorecard visibility) → development (corrective actions, capacity plans) → suspension/blacklist with reason taxonomy → offboarding (open-commitment check).

## 5. Business Scenario Coverage Matrix (edge-case charter)
Every module volume must explicitly cover: multi-currency + FX drift between events; partials at every step; cancellations at every state with financial unwind; amendments with version history; back-dated documents vs closed periods; tolerance breaches (accept/reject/escalate paths); duplicate detection (vendor/invoice/document); force-majeure holds; year-end cutover; and Bangladesh specifics (SRO expiry mid-shipment, assessment lane escalation Y→R, port congestion demurrage waivers, BB circular changes with effective dating).

## 6. Out of Scope (v2.0)
Selling-side (sales/CRM/export LC beyond BTB master linkage), production planning/MRP (consumes plans via API), full accounting GL (posts to it, doesn't replace it), HR/payroll, transport fleet management.

## 7. Assumptions & Dependencies
NBR tariff data maintained as a platform content service (weekly ops pipeline); ASYCUDA/BB integrations begin as structured mirror-entry + document upload (APIs adopted when public); banks integrate via SWIFT copy upload/email ingestion pre-API; tenants provide chart-of-accounts mapping during onboarding.

## 8. High-Level Process Flow

```
DEMAND ──► PLAN ──► SOURCE ──► CONTRACT
                       │
                       ▼
              PR ──► APPROVE ──► PO ◄─── contract call-off
                                  │
              ┌─── local ─────────┴───────── import ───┐
              ▼                                        ▼
            GRN ◄── deliver                PI ─► INS ─► LC ─► SHIP ─► DOCS
              │                                        │
              ▼                                        ▼
           QC/PUTAWAY                    BoE ─► ASSESS ─► DUTY ─► RELEASE
              │                                        │
              ▼                                        ▼
         INVOICE ─► 3-WAY MATCH ─► AP          GRN ─► COST SHEET ─► FINALIZE
              │                                        │
              └────────────► GL / BUDGET / ANALYTICS ◄─┘
```
