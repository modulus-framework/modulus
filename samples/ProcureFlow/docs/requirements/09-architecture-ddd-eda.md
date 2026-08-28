# 09 — Architecture: DDD, CQRS & Event-Driven Design

## 1. Bounded Contexts & Context Map

```
┌─────────────────────────────  ProcureFlow Cell  ─────────────────────────────┐
│                                                                              │
│  Identity & Tenancy (Keycloak + control plane)         [Generic]            │
│                                                                              │
│  ┌──────────────┐   awards    ┌──────────────┐  PO ref  ┌────────────────┐  │
│  │  Sourcing     │──────────▶│  Procurement  │────────▶│  Import         │  │
│  │ (RFQ/RFP/Bid) │           │ (PR/PO/Contr.)│          │ Logistics      │  │
│  └──────┬───────┘            └──────┬───────┘          │ (File/Ship/    │  │
│         │ vendor data               │ budget           │  Clearance)    │  │
│  ┌──────▼───────┐            ┌──────▼───────┐          └───┬─────┬──────┘  │
│  │   Vendor      │            │   Budgeting  │              │     │         │
│  │ (Master/Qual/ │            └──────────────┘     duty/ ┌──▼──┐  │costs    │
│  │  Scorecard)   │                                 docs  │Trade│  │         │
│  └──────────────┘                                        │Fin. │  │         │
│  ┌──────────────┐  rates  ┌────────────────┐            │(LC/ │  │         │
│  │ Customs & Tax │◀───────│  Landed Cost    │◀───────────┤ TT/ │◀─┘         │
│  │ (HS/Rates/    │  calc  │  & Costing      │  charges   │Loan)│            │
│  │  Assessment)  │───────▶│                 │            └─────┘            │
│  └──────────────┘         └───────┬────────┘                                │
│  ┌──────────────┐  GRN qty        │ final cost   ┌──────────────────┐       │
│  │  Inventory    │────────────────┘─────────────▶│ Finance Posting  │       │
│  │ (GRN/Batch/   │                               │ (AP/GL/Accrual)  │       │
│  │  Valuation)   │                               └──────────────────┘       │
│  └──────────────┘                                                           │
│  Intelligence (Forecast/Feasibility/Risk)   [Supporting, reads all marts]   │
│  Workflow & Tasks │ Notifications │ Document Vault │ Audit   [Generic]      │
└──────────────────────────────────────────────────────────────────────────────┘
```
**Relationships:** Procurement↔Import = Partnership (shared PO/file linkage contract). Customs&Tax → Landed Cost = Supplier/Conformist (rate API). Intelligence = separate ways (consumes published language events + marts only). All cross-context integration via **published domain events + thin query APIs**; no shared tables across contexts (separate schemas in same DB: `vendor.*`, `proc.*`, `import.*`, `tradefin.*`, `customs.*`, `costing.*`, `inv.*`, `fin.*`, `platform.*`).

**Deployment:** modular monolith (one ASP.NET host, context = assembly with enforced boundaries via ArchUnitNET tests) + separately deployed services: `intelligence-api` (Python ML), `worker` (outbox dispatcher, saga timers, report jobs), `search-indexer`. Contexts are extraction-ready (own schema, own events) — split only when scale demands.

## 2. Aggregates, Entities, Value Objects (per context — principal ones)

| Context | Aggregate (root) | Entities inside | Value Objects |
|---|---|---|---|
| Vendor | Vendor | Contact, Address, BankAccount, CategoryQualification, Document | TaxIds, BankDetails, RiskGrade |
| | Scorecard (per vendor-period) | MetricLine | Grade, Weighting |
| Sourcing | SourcingCase | Line, Invitation, Bid (entity w/ BidLines), Score | Deadline, EnvelopeWeights |
| | Award | AwardLine | SplitAllocation, Rationale |
| Procurement | PurchaseRequisition | PrLine | NeedBy, EstimatedAmount |
| | PurchaseOrder | PoLine, Revision, Acknowledgement | Incoterm, PaymentMode, FeasibilitySnapshot, Money, Quantity |
| | Contract | ContractLine, Milestone | ValidityWindow, ConsumptionCap |
| Budgeting | Budget | BudgetLine, BudgetTxn | FiscalYear, Slab |
| Import | ImportFile | DocumentLink, MilestoneLog, CostElementRef | FileState, PortPair |
| | Shipment | Milestone, Container, TransportDocument, EtaRevision | ContainerNo(ISO6346), Mode |
| | ProformaInvoice / CommercialInvoice / PackingList | lines | DocVersion |
| | InsurancePolicy | Declaration, Claim | CoverValue |
| Trade Finance | LetterOfCredit | Amendment, Presentation, MarginEvent, Charge, Retirement | Tenor, Tolerance, MaturityDate |
| | MasterExportLc | B2bLink | Entitlement |
| | TtPayment, ImportLoan, BankFacility | schedule lines | RateBasis |
| Customs&Tax | TariffLine (HS) | DutyRate rows | HsCode, RatePeriod |
| | BillOfEntry | BoeLine, Variance, Challan | Lane, AssessableValue |
| | SroBenefit | conditions | BenefitMode |
| Costing | LandedCostSheet | CostElement, Allocation, LineLandedCost, Version | Driver, Stage, UnitCost |
| Inventory | Grn | GrnLine, QcInspection | Tolerance |
| | Batch / SerialUnit | — | ExpiryDates |
| | ValuationLedger (per item-site) | LedgerEntry | CostLayer |
| Finance | ApInvoiceVoucher, JournalBatch, AccrualRun | lines | AccountRef, PostingPeriod |
| Platform | WorkflowInstance | Task, Transition | Definition(Version), SLA |

**VO conventions:** `Money {amount: decimal(18,4), currency}` with arithmetic guarded by currency equality; `Quantity {value, uom}` with UoM conversion service; `HsCode` validates 8-digit + chapter; all ids strongly-typed (`PoId`, `FileId`) via source-generated value records.

**Aggregate rules:** invariants enforced inside roots (e.g., `PurchaseOrder.Submit()` requires lines>0, import fields complete, runs feasibility gate state); cross-aggregate consistency only via events/sagas; optimistic concurrency (`xmin` mapped to version) on all roots.

## 3. Domain Services
`DutyCalculator`, `AllocationEngine` (doc 07.1), `EntitlementCalculator` (B2B LC), `MatchingService` (3-way), `ToleranceEvaluator`, `NumberingService` (per-tenant sequences, gapless option for legal docs), `FxRateService` (dated, source-ranked), `EligibilityChecker` (LC prerequisites), `SodValidator` (segregation rules into workflow).

## 4. Domain Events (published language — past tense, versioned `v1`)
Vendor: `VendorQualified`, `VendorBlacklisted`, `ScorecardPublished`, `BankAccountChanged`.
Sourcing: `BidReceived`, `CaseAwarded`.
Procurement: `PrApproved`, `PoSubmitted`, `FeasibilityEvaluated`, `PoApproved`, `PoDispatched`, `PoAcknowledged`, `PoRevised`, `PoClosed`.
Import: `ImportFileOpened`, `PiAccepted`, `ShipmentBooked`, `ShipmentDeparted`, `ShipmentArrived`, `DocumentsEndorsed`, `BoeSubmitted`, `AssessmentRecorded`, `AssessmentVarianceRaised`, `DutyPaid`, `ConsignmentReleased`, `FileMilestoneSlaBreached`.
TradeFin: `LcApplied/Issued/Amended`, `DocumentsPresented`, `DiscrepancyDecided`, `BillAccepted`, `LcRetired`, `MarginBlocked/Released`, `TtExecuted`, `LoanCreated`, `ObligationDueSoon`.
Customs: `DutyRatesChanged`, `ChallanRecorded`.
Costing: `CostElementRecorded`, `CostSheetReady`, `LandedCostFinalized`, `CostAdjusted`.
Inventory: `GrnPosted`, `QcDecided`, `BatchCreated`, `InventoryRevalued`.
Finance: `JournalPosted`, `InvoiceMatched`, `MatchExceptionRaised`, `PaymentProposed/Settled`.
Platform: `TaskCreated/Completed`, `WorkflowCompleted/Rejected`.

**Envelope:** `{eventId, type, version, tenantId, companyId, aggregate{type,id}, occurredAt, correlationId, causationId, actor, payload}` — JSON Schema registry in repo; consumers tolerant-reader.

## 5. CQRS Implementation (.NET 9)

```
src/
  Api/                      // ASP.NET minimal APIs per context, versioned /v1
  Contexts/
    Procurement/
      Domain/               // aggregates, VOs, events, services (no deps)
      Application/          // MediatR commands/queries, validators (FluentValidation),
                            // authorization behaviors, transaction behavior
      Infrastructure/       // Dapper repositories, outbox writer, query DAOs
    Import/  TradeFinance/  Customs/  Costing/  Inventory/  Finance/  Vendor/  Sourcing/
  Platform/                 // tenancy, workflow, notifications, audit, document vault
  Workers/                  // outbox dispatcher, saga manager, schedulers, indexer
  BuildingBlocks/           // Result, Money, ids, pipeline behaviors, event bus abstractions
```
- **Commands:** MediatR `IRequest<Result<T>>`; pipeline: Logging → Validation → Authorization (capability+scope) → TenantTransaction (opens conn, `SET LOCAL app.tenant_id`, tx) → Idempotency (key table) → Handler → OutboxFlush → AuditCapture.
- **Writes:** repositories use Dapper with explicit SQL; aggregates rehydrated via multi-mapping; domain events collected on the root and written to `platform.outbox` in the same transaction.
- **Queries:** bypass domain — Dapper straight to read models/views; list endpoints take cursor pagination; heavy lists served from OpenSearch projections.
- **Read models:** per-context projection tables (e.g., `import.file_summary`, `tradefin.exposure_by_bank`) maintained by event consumers — rebuildable from event archive (S3) for recovery.

## 6. Event-Driven Architecture (AWS)
```
outbox (Postgres) → Dispatcher worker (poll/LISTEN, batch 100, ordered per aggregate)
  → EventBridge custom bus "procureflow-{cell}" (detail-type = event type)
     → Rules → SQS queues per consumer group (projections, notifications, search-indexer,
       intelligence-feature-builder, webhook-egress, saga-manager) each with DLQ (maxReceive 5)
     → Kinesis Firehose rule → S3 event archive (parquet, partitioned dt/tenant) → Athena
```
- Consumers idempotent via `processed_event (consumer, event_id)` guard; ordering needed only per-aggregate (FIFO SQS for saga-manager + projections keyed by aggregateId; standard queues elsewhere).
- Backpressure: queue-depth alarms → worker autoscaling (ECS scale on ApproximateNumberOfMessages).

## 7. Saga Design (process managers; state persisted `platform.saga_instance`)

### 7.1 ImportFulfilmentSaga (per ImportFile) — the backbone
```
Trigger: PoApproved(import)
Steps (event-driven, with timeout timers):
  ImportFileOpened → expect PiAccepted        (timer: 14d → nudge buyer)
  PiAccepted → command EnsureInsurance        → expect PolicyRecorded
  PolicyRecorded → expect LcIssued | TtExecuted(advance)   (per payment mode)
  LcIssued → expect ShipmentDeparted          (timer: latestShipment-7d → risk alert)
  ShipmentDeparted → expect ShipmentArrived   (delay-prediction check on ETA revisions)
  ShipmentArrived → command CreateClearanceChecklist → expect DutyPaid
  DutyPaid → expect ConsignmentReleased       (timer: freeTime×0.7 → demurrage alert)
  ConsignmentReleased → expect GrnPosted
  GrnPosted → command SeedCostSheetActualsCheck → expect CostSheetReady
  CostSheetReady → human task (finalize) → LandedCostFinalized
  LandedCostFinalized → commands: RevalueInventory, PostJournals, UpdateScorecards,
                        PublishVarianceReport → FileClosed
Compensations: PoCancelled → close file, release budget, alert if LC issued (manual bank
cancellation task); LcCancelled mid-saga → file Held state + task.
```

### 7.2 LcSettlementSaga
`BillAccepted → schedule maturity timers (T-7/3/1 notifications) → at maturity: expect LcRetired; if retirement source=loan → command CreateLoan → LoanCreated → release margin → post FX → done; overdue → escalating alerts + Held flag on bank facility.`

### 7.3 CostFinalizationSaga
Coordinates LandedCostFinalized fan-out with guaranteed completion: revaluation → GL batch → clearing-account check → on residue: reconciliation task; retries with exponential backoff; manual resolution console for poisoned steps.

### 7.4 VendorOnboardingSaga
Registration → screening (async sanctions check) → qualification workflow → portal account provisioning (Keycloak) → AVL update.

**Saga infrastructure:** saga-manager worker consumes FIFO queue, loads instance, applies transition (state machine table per saga type), persists, emits commands via MediatR over internal API or queue; timers via EventBridge Scheduler one-time schedules targeting the saga queue.

## 8. API Design
- REST, versioned `/api/v1/{context}/...`; OpenAPI 3.1 generated; idempotency-key header honored on POST; problem+json errors with domain error codes; ETag/If-Match for optimistic concurrency on PUT.
- Webhooks (Enterprise): tenant-registered endpoints, HMAC-signed, retry with backoff, event filter subscriptions.
- Internal context-to-context queries via in-process interfaces (monolith) behind `IContextGateway` abstractions to keep extraction possible.
