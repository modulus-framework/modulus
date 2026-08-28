# ModulusSampleErp Architecture

## Overview

ModulusSampleErp is a **modular monolith** demonstrating enterprise-grade B2B ERP capabilities using the Modulus framework. Six business modules (Catalog, Partners, Inventory, Sales, Purchasing, Billing) model a wholesale distributor. Seven platform modules provide infrastructure (Identity, Tenants, Settings, Features, VirtualFileExplorer, Notifications, Media).

---

## Architectural Layers

Every module follows **Clean Architecture** with 4 layers:

```
┌─────────────────────────────────────────────────────────┐
│ Presentation Layer                                      │
│ - REPR Endpoints (Endpoint<TRequest, TResponse>)       │
│ - IntegrationEventHandlers (subscribe to events)       │
│ - Hubs (SignalR, optional)                             │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ Application Layer (Business Rules)                      │
│ - CQRS Commands & Handlers                             │
│ - CQRS Queries & Handlers                              │
│ - Sagas (multi-step workflows)                         │
│ - DomainEventHandlers                                  │
│ - Application Services                                 │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ Domain Layer (Invariants & Facts)                       │
│ - AggregateRoots (with DomainEvents)                   │
│ - Entities                                             │
│ - ValueObjects                                         │
│ - Specifications (complex queries)                     │
│ - Domain Errors & Enums                                │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ Infrastructure Layer                                    │
│ - ModuleDbContext (EF Core)                            │
│ - Repositories (Entity access)                         │
│ - PublicApi (cross-module sync calls, optional)        │
│ - IntegrationEvents (IIntegrationEvent records)        │
│ - Configuration (EF mappings, external services)       │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│ Shared Kernel (ModulusSample.Shared.*)                 │
│ - Result<T> pattern                                    │
│ - AggregateRoot<TId> base class                        │
│ - ValueObject conventions                             │
│ - Specification<T> pattern                            │
│ - Exception mappers                                    │
└─────────────────────────────────────────────────────────┘
```

---

## Cross-Module Communication

### 1. Synchronous (In-Process)

**Use case**: Quick reads needed during request handling.

**Pattern**: `I{Module}PublicApi` interface injected into handler.

```csharp
// Sales module: CreateSalesOrderCommandHandler
public class CreateSalesOrderCommandHandler : ICommandHandler<CreateSalesOrderCommand>
{
    private readonly IPartnerPublicApi _partnerApi; // Injected

    public async Task<Result> Handle(CreateSalesOrderCommand request, CancellationToken ct)
    {
        // Quick validation: customer exists?
        var customer = await _partnerApi.GetCustomerAsync(request.CustomerId, ct);
        if (customer is null)
            return Result.Failure(Error.NotFound("Customer.NotFound", ""));

        // Proceed with order creation
        ...
    }
}
```

**Restriction**: Only reference `.IntegrationEvents` or `.Contracts` projects, never `.Application` or `.Infrastructure`.

### 2. Asynchronous (Guaranteed Delivery)

**Use case**: State changes that need reliable cross-module notification.

**Pattern**: Domain event → DomainEventHandler → IntegrationEvent → Outbox → RabbitMQ → Inbox → IntegrationEventHandler

```csharp
// Sales module: OrderPlacedDomainEvent
public sealed record SalesOrderSubmittedDomainEvent(
    Guid EventId,
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    DateTime OccurredOnUtc
) : IDomainEvent;

// Handler: converts to integration event
public class SalesOrderSubmittedDomainEventHandler : IDomainEventHandler<SalesOrderSubmittedDomainEvent>
{
    private readonly IEventBus _eventBus;

    public async Task Handle(SalesOrderSubmittedDomainEvent @event, CancellationToken ct)
    {
        var integrationEvent = new SalesOrderSubmittedIntegrationEvent(
            Guid.NewGuid(),
            @event.OrderId,
            @event.CustomerId,
            @event.Total,
            @event.OccurredOnUtc);

        await _eventBus.PublishAsync(integrationEvent, ct); // Outbox handles delivery
    }
}

// Inventory module: Subscription handler
public class SalesOrderSubmittedIntegrationEventHandler :
    IIntegrationEventHandler<SalesOrderSubmittedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public async Task Handle(SalesOrderSubmittedIntegrationEvent @event, CancellationToken ct)
    {
        // Reserve stock when order submitted
        var command = new ReserveStockCommand(
            @event.OrderId,
            /* line items from event */);

        await _mediator.SendAsync(command, ct);
    }
}
```

**Flow**: Guaranteed by Modulus.Outbox (saves to outbox table in same transaction) + Modulus.Sagas (Quartz job polls outbox → publishes to RabbitMQ) + Inbox (receives, deduplicates, processes).

---

## Module Domain Models

### Catalog Module

```
┌─────────────────────────────────────────────────────────┐
│ Catalog Schema                                          │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: Product                                  │
│ ├─ ProductId (Guid)                                     │
│ ├─ Sku (string, unique)                                 │
│ ├─ Name, Description                                    │
│ ├─ UnitPrice (Money) [visible to all]                   │
│ ├─ CostPrice (Money) [Classified: Finance only]         │
│ ├─ Margin (calculated) [Classified: Finance only]       │
│ ├─ Category (FK to ProductCategory)                     │
│ ├─ Status (Active | Discontinued | Draft)               │
│ ├─ CreatedByUserId, TenantId                            │
│ └─ DomainEvents: ProductCreated, ProductUpdated, ...    │
│                                                         │
│ AggregateRoot: ProductCategory                          │
│ ├─ CategoryId (Guid)                                    │
│ ├─ Name, Code                                           │
│ ├─ ParentCategoryId (nullable, hierarchy)               │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ ValueObject: UnitOfMeasure                              │
│ ├─ Id (Guid)                                            │
│ ├─ Code (PCS, BOX, KG, ...)                             │
│ └─ Name                                                 │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- Redis caching with tag-based invalidation (`product:*`, `category:*`)
- Field security: CostPrice & Margin `[Classified]`
- Media integration: images from Media module

### Partners Module

```
┌─────────────────────────────────────────────────────────┐
│ Partners Schema                                         │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: Customer                                 │
│ ├─ CustomerId (Guid)                                    │
│ ├─ CompanyName (encrypted)                              │
│ ├─ TaxId (encrypted)                                    │
│ ├─ PrimaryContact (Contact entity, encrypted email)     │
│ ├─ Address (encrypted)                                  │
│ ├─ CreditLimit (Money)                                  │
│ ├─ Status (Active | Inactive | Suspended)               │
│ ├─ OwnedByUserId (Sales rep → data scope)               │
│ ├─ TenantId                                             │
│ └─ DomainEvents: CustomerCreated, CreditLimitReached... │
│                                                         │
│ AggregateRoot: Supplier                                 │
│ ├─ SupplierId (Guid)                                    │
│ ├─ CompanyName (encrypted)                              │
│ ├─ TaxId (encrypted)                                    │
│ ├─ Contact (Contact entity)                             │
│ ├─ PaymentTerms (Net 30, ...)                           │
│ ├─ Status                                               │
│ ├─ TenantId                                             │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ Entity: Contact                                         │
│ ├─ ContactId (Guid)                                     │
│ ├─ Email (encrypted)                                    │
│ ├─ Phone (encrypted)                                    │
│ ├─ Name                                                 │
│ └─ Title                                                │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- PII encryption at rest via `IPersonalDataProtector`
- Data scope: `IHasOwner` → Sales rep sees only their customers
- Org scope: Scoped to tenant

### Inventory Module

```
┌─────────────────────────────────────────────────────────┐
│ Inventory Schema                                        │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: Warehouse (IHasOrgUnit)                  │
│ ├─ WarehouseId (Guid)                                   │
│ ├─ Name, Location                                       │
│ ├─ OrgUnitId (Guid) → org scope filtering               │
│ ├─ Status (Active | Inactive)                           │
│ ├─ TenantId                                             │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ AggregateRoot: StockItem                                │
│ ├─ StockItemId (Guid)                                   │
│ ├─ ProductId (Guid) → FK to Catalog                     │
│ ├─ WarehouseId (Guid)                                   │
│ ├─ AvailableQuantity (Quantity ValueObject)             │
│ ├─ ReservedQuantity (Quantity)                          │
│ ├─ ReorderPoint (Quantity)                              │
│ ├─ LastCountedUtc                                       │
│ ├─ Version (int) → optimistic concurrency               │
│ ├─ TenantId                                             │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ Entity: StockReservation                                │
│ ├─ ReservationId (Guid)                                 │
│ ├─ StockItemId (Guid)                                   │
│ ├─ QuantityReserved (Quantity)                          │
│ ├─ ReservationReason (SalesOrder | PurchaseOrder | ...) │
│ ├─ ReferenceId (Guid) → e.g., SalesOrderId              │
│ └─ Status (Reserved | Committed | Cancelled)            │
│                                                         │
│ AggregateRoot: StockMovement (audit trail)              │
│ ├─ MovementId (Guid)                                    │
│ ├─ StockItemId (Guid)                                   │
│ ├─ MovementType (In | Out | Adjustment | Transfer)      │
│ ├─ Quantity (signed Quantity)                           │
│ ├─ Reason, DocumentId                                   │
│ ├─ CreatedUtc, CreatedByUserId                          │
│ └─ (immutable)                                          │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- `IHasOrgUnit`: Warehouse scope → user sees only their branch's warehouses
- Optimistic concurrency: `Version` on StockItem (handles concurrent reductions)
- Integration event subscribers: `SalesOrderCreatedIntegrationEvent` (attempt reservation), `GoodsReceiptConfirmedIntegrationEvent` (increase qty)
- Quartz job: `ReorderPointCheckJob` (hourly) → flag low stock

### Sales Module

```
┌─────────────────────────────────────────────────────────┐
│ Sales Schema                                            │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: SalesOrder (IHasOrgUnit)                 │
│ ├─ SalesOrderId (Guid)                                  │
│ ├─ CustomerId (Guid) → FK to Partners                   │
│ ├─ OrderNumber (string, unique)                         │
│ ├─ OrderDate                                            │
│ ├─ Status (Draft | Submitted | Confirmed | Picked |     │
│ │          Shipped | Invoiced | Cancelled)              │
│ ├─ Lines (Collection: SalesOrderLine)                   │
│ ├─ Total (Money, calculated)                            │
│ ├─ DiscountAmount (Money)                               │
│ ├─ DiscountApprovedBy (Guid, nullable)                  │
│ │  [if DiscountAmount/Total > 5%, requires approval]    │
│ ├─ ShipToAddress                                        │
│ ├─ OrgUnitId (Guid) → org scope (branch-scoped orders)  │
│ ├─ TenantId, CreatedByUserId                            │
│ └─ DomainEvents: OrderCreated, Submitted, Confirmed,    │
│    Cancelled, ...                                       │
│                                                         │
│ Entity: SalesOrderLine                                  │
│ ├─ LineId (Guid)                                        │
│ ├─ ProductId (Guid)                                     │
│ ├─ Quantity (Quantity ValueObject)                      │
│ ├─ UnitPrice (Money)                                    │
│ └─ LineTotal (Money, calculated)                        │
│                                                         │
│ AggregateRoot: Shipment                                 │
│ ├─ ShipmentId (Guid)                                    │
│ ├─ SalesOrderId (Guid)                                  │
│ ├─ Lines (ShipmentLine collection)                      │
│ ├─ ShippedUtc                                           │
│ ├─ TrackingNumber                                       │
│ └─ Status (Pending | Shipped | Delivered)               │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│ Saga: OrderFulfillmentSaga                              │
├─────────────────────────────────────────────────────────┤
│ Data: OrderId, CustomerId, Lines, CurrentStep           │
│                                                         │
│ Start: SalesOrderSubmittedDomainEvent                   │
│   → Send ReserveStockCommand to Inventory               │
│                                                         │
│ Step 1 (Stock Reserved):                                │
│   → Handle StockReservedIntegrationEvent                │
│   → Send PickOrderCommand                               │
│                                                         │
│ Step 2 (Order Picked):                                  │
│   → Handle OrderPickedIntegrationEvent                  │
│   → Send ShipOrderCommand                               │
│                                                         │
│ Step 3 (Order Shipped):                                 │
│   → Handle OrderShippedIntegrationEvent                 │
│   → Send CreateInvoiceCommand to Billing                │
│                                                         │
│ Step 4 (Invoice Created):                               │
│   → Handle InvoiceCreatedIntegrationEvent               │
│   → Saga completes, publish OrderFulfilledEvent         │
│                                                         │
│ Compensation (on any failure):                          │
│   → Send ReleaseStockReservationCommand                 │
│   → Transition order to Cancelled                       │
│   → Publish OrderCancelledEvent                         │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- Workflow policy: State machine (no back-stepping)
- Saga with compensation: Handles stock unavailability gracefully
- Discount approval: Workflow transition validation (SoD)
- Org scope: Branch-level sales filtering

### Purchasing Module

```
┌─────────────────────────────────────────────────────────┐
│ Purchasing Schema                                       │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: PurchaseRequisition                       │
│ ├─ RequisitionId (Guid)                                 │
│ ├─ RequisitionNumber (string, unique)                   │
│ ├─ Status (Draft | Submitted | AwaitingApproval |       │
│ │          Approved | Rejected | Received)              │
│ ├─ RequesterUserId (Guid) [Buyer]                       │
│ ├─ SubmittedUtc (DateTime, nullable)                    │
│ ├─ ApprovedByUserId (Guid, nullable) [Manager]          │
│ │  [SoD: RequesterUserId ≠ ApprovedByUserId]             │
│ ├─ ApprovedUtc (DateTime, nullable)                     │
│ ├─ Lines (Collection: RequisitionLine)                  │
│ ├─ Total (Money)                                        │
│ ├─ OrgUnitId (Guid) → part of approval hierarchy        │
│ ├─ TenantId                                             │
│ └─ DomainEvents: Created, Submitted, Approved,          │
│    Rejected, ...                                        │
│                                                         │
│ AggregateRoot: PurchaseOrder                            │
│ ├─ OrderId (Guid)                                       │
│ ├─ OrderNumber (string, unique)                         │
│ ├─ RequisitionId (Guid) → FK to requisition             │
│ ├─ SupplierId (Guid) → FK to Partners                   │
│ ├─ OrderDate                                            │
│ ├─ Status (Draft | Sent | AwaitingReceipt |             │
│ │          PartiallyReceived | Received | Cancelled)    │
│ ├─ Lines (Collection: PurchaseOrderLine)                │
│ ├─ Total (Money)                                        │
│ ├─ TenantId                                             │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ AggregateRoot: GoodsReceipt                             │
│ ├─ ReceiptId (Guid)                                     │
│ ├─ OrderId (Guid)                                       │
│ ├─ ReceiptDate                                          │
│ ├─ Lines (Collection: ReceiptLine w/ qty received)      │
│ ├─ Status (Pending | Confirmed | Rejected)              │
│ ├─ ConfirmedByUserId (Guid, warehouse manager)          │
│ ├─ ConfirmedUtc (DateTime, nullable)                    │
│ └─ DomainEvents: Confirmed, ...                         │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- SoD enforcement: Approval denies if requester == approver
- Delegation support: Deputy approves in manager's place (time-bounded)
- Approval queue spec: `PurchaseRequisitionsAwaitingApprovalSpec` (reusable query)
- Workflow policy: State machine transitions

### Billing Module

```
┌─────────────────────────────────────────────────────────┐
│ Billing Schema                                          │
├─────────────────────────────────────────────────────────┤
│ AggregateRoot: Invoice                                  │
│ ├─ InvoiceId (Guid)                                     │
│ ├─ InvoiceNumber (string, unique)                       │
│ ├─ SalesOrderId (Guid) → FK to Sales                    │
│ ├─ CustomerId (Guid) → FK to Partners                   │
│ ├─ InvoiceDate                                          │
│ ├─ DueDate                                              │
│ ├─ Status (Draft | Issued | Sent | PartiallyPaid |      │
│ │          Paid | Overdue | WriteOff)                   │
│ ├─ Lines (Collection: InvoiceLine)                      │
│ ├─ Total (Money)                                        │
│ ├─ AmountPaid (Money)                                   │
│ ├─ TaxAmount (Money) [Classified: Finance only]         │
│ ├─ Margin (calculated) [Classified: Finance only]       │
│ ├─ TenantId                                             │
│ └─ DomainEvents: Created, Issued, Paid, ...             │
│                                                         │
│ Entity: InvoiceLine                                     │
│ ├─ LineId (Guid)                                        │
│ ├─ ProductId (Guid)                                     │
│ ├─ Quantity (Quantity)                                  │
│ ├─ UnitPrice (Money)                                    │
│ ├─ LineTotal (Money)                                    │
│ └─ CostPrice (Money) [Classified: Finance only]         │
│    [for margin calculation: (UnitPrice - CostPrice) /   │
│     UnitPrice]                                          │
│                                                         │
│ AggregateRoot: Payment                                  │
│ ├─ PaymentId (Guid)                                     │
│ ├─ InvoiceId (Guid)                                     │
│ ├─ PaymentDate                                          │
│ ├─ Amount (Money)                                       │
│ ├─ Method (Check | Wire | CreditCard | ...)             │
│ ├─ Status (Pending | Confirmed | Cleared)               │
│ ├─ ConfirmedUtc (DateTime, nullable)                    │
│ └─ DomainEvents: ...                                    │
│                                                         │
│ AggregateRoot: CreditNote                               │
│ ├─ CreditNoteId (Guid)                                  │
│ ├─ CreditNoteNumber (string)                            │
│ ├─ InvoiceId (Guid)                                     │
│ ├─ Reason (Returns | Damaged | Discount | ...)          │
│ ├─ Amount (Money)                                       │
│ ├─ Status (Draft | Issued | Applied)                    │
│ ├─ AppliedToPaymentId (Guid, nullable)                  │
│ └─ DomainEvents: ...                                    │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│ Background Jobs (Quartz)                                │
├─────────────────────────────────────────────────────────┤
│ ARAging (hourly):                                       │
│   → Query all invoices with PaymentDueDate <= now       │
│   → Classify: Current | 30days | 60days | 90plus        │
│   → Update invoice aging status                        │
│                                                         │
│ OutstandingBalance (daily):                             │
│   → Find invoices > 60 days overdue                      │
│   → Email Finance: list, total amount due               │
│                                                         │
│ WriteOffEligibility (weekly):                           │
│   → Find invoices > 12 months overdue                    │
│   → Flag as eligible for write-off                      │
│   → Notify Finance for review                          │
└─────────────────────────────────────────────────────────┘
```

**Key features**:
- Field security: TaxAmount & Margin `[Classified]` (Finance only)
- Cross-module events: Subscribe to Sales & Purchasing
- Cross-module calculation: Pull CostPrice from Catalog for margin
- Feature entitlements: AR-aging report (Enterprise only)
- Background jobs: 3 Quartz jobs for aging/outstanding/write-off

---

## Value Objects

Standard patterns for monetary and quantity-based attributes:

### Money

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0)
            return Result.Failure<Money>(
                Error.Validation("Money.Negative", "Amount cannot be negative"));

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            return Result.Failure<Money>(
                Error.Validation("Money.InvalidCurrency", "Currency must be 3-letter ISO code"));

        return Result.Success(new Money(amount, currency));
    }
}

// Usage: UnitPrice: Money, Total: Money, AmountPaid: Money
```

### Quantity

```csharp
public readonly record struct Quantity(int Value)
{
    public static Result<Quantity> Create(int value)
    {
        if (value <= 0)
            return Result.Failure<Quantity>(
                Error.Validation("Quantity.Invalid", "Quantity must be > 0"));

        return Result.Success(new Quantity(value));
    }

    public static Quantity operator +(Quantity left, Quantity right)
        => new(left.Value + right.Value);

    public static Quantity operator -(Quantity left, Quantity right)
    {
        var result = left.Value - right.Value;
        return result >= 0 ? new(result) : throw new InvalidOperationException("Quantity cannot be negative");
    }
}

// Usage: AvailableQuantity: Quantity, QuantityOrdered: Quantity
```

### Email

```csharp
public readonly record struct Email(string Value)
{
    public static Result<Email> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("@"))
            return Result.Failure<Email>(
                Error.Validation("Email.Invalid", "Invalid email format"));

        return Result.Success(new Email(value));
    }
}

// Usage: Contact.Email: Email (encrypted)
```

---

## Specification Pattern

For complex, reusable query logic:

```csharp
// Base class (in Shared.Application)
public abstract class Specification<T> where T : class
{
    public Expression<Func<T, bool>>? Criteria { get; protected set; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public int? PageNumber { get; protected set; }
    public int? PageSize { get; protected set; }

    protected void AddInclude(Expression<Func<T, object>> include)
        => Includes.Add(include);
}

// Example: Approval queue (Purchasing)
public class PurchaseRequisitionsAwaitingApprovalSpec : Specification<PurchaseRequisition>
{
    public PurchaseRequisitionsAwaitingApprovalSpec(Guid approverId)
    {
        // Criteria: awaiting approval in my org unit OR delegated to me
        Criteria = req => req.Status == RequisitionStatus.AwaitingApproval &&
            (req.OrgUnitId == approverId || req.DelegatedToUserId == approverId);

        // Eager load related entities
        AddInclude(r => r.Lines);
        AddInclude(r => r.Requester);
    }
}

// Usage in repository
public async Task<List<PurchaseRequisition>> ListBySpecificationAsync(
    Specification<PurchaseRequisition> spec,
    CancellationToken ct)
{
    var query = DbSet<PurchaseRequisition>.Where(spec.Criteria);

    foreach (var include in spec.Includes)
        query = query.Include(include);

    if (spec.PageNumber.HasValue && spec.PageSize.HasValue)
        query = query
            .Skip((spec.PageNumber.Value - 1) * spec.PageSize.Value)
            .Take(spec.PageSize.Value);

    return await query.ToListAsync(ct);
}
```

---

## Data Flow Examples

### Scenario 1: Order-to-Cash Saga

```
Alice (Sales Rep) creates SalesOrder
        ↓
API: POST /api/sales/orders → CreateSalesOrderCommand
        ↓
CreateSalesOrderCommandHandler
  - Validate customer exists (via IPartnerPublicApi)
  - Validate products exist (via ICatalogPublicApi)
  - Create Order aggregate (Draft status)
  - Save to DB (SalesOrder table)
        ↓
Order saved, DomainEvent raised: SalesOrderCreatedDomainEvent
        ↓
Alice submits order
        ↓
API: POST /api/sales/orders/{id}/submit → SubmitSalesOrderCommand
        ↓
SubmitSalesOrderCommandHandler
  - Validate order in Draft status
  - Transition to Submitted
  - Raise DomainEvent: SalesOrderSubmittedDomainEvent
  - Save to DB
        ↓
DomainEvent: SalesOrderSubmittedDomainEvent
        ↓
DomainEventHandler: SalesOrderSubmittedDomainEventHandler
  - Convert to IntegrationEvent: SalesOrderSubmittedIntegrationEvent
  - Publish via IEventBus (saves to Outbox table)
        ↓
Outbox table row created (in same transaction)
        ↓
Quartz job: ProcessOutboxJob (runs every 30s)
  - Read unprocessed outbox rows
  - Publish to RabbitMQ (via Rebus)
  - Mark as processed
        ↓
RabbitMQ routes message to inventory-queue
        ↓
Inventory service receives: SalesOrderSubmittedIntegrationEvent
  - Inbox handler stores message (deduplication via EventId)
  - IntegrationEventHandler: SalesOrderSubmittedIntegrationEventHandler
  - Send ReserveStockCommand to Inventory
        ↓
ReserveStockCommandHandler
  - Look up StockItem for each line
  - Check AvailableQuantity >= Quantity
  - Create StockReservation (status=Reserved)
  - Increment ReservedQuantity
  - Raise DomainEvent: StockReservedDomainEvent
  - Save (with Version check for optimistic concurrency)
        ↓
DomainEvent: StockReservedDomainEvent
        ↓
Publish IntegrationEvent: StockReservedIntegrationEvent → Outbox
        ↓
Sales service receives: StockReservedIntegrationEvent
  - Saga step handler: OrderFulfillmentSaga
  - Transition order: Submitted → Confirmed
  - (Simplified: in real scenario, more steps)
        ↓
[Continuing steps: Pick → Ship → Invoice → Complete]
        ↓
OrderFulfillmentSaga completes
  - Publish OrderFulfilledIntegrationEvent
        ↓
Billing service receives: OrderFulfilledIntegrationEvent
  - IntegrationEventHandler: OrderFulfilledIntegrationEventHandler
  - Create Invoice (auto-issued, ready to send)
        ↓
Order fulfilled end-to-end
```

### Scenario 2: SoD + Delegation

```
Diana (Buyer) creates PurchaseRequisition
        ↓
CreatePurchaseRequisitionCommand
        ↓
CreatePurchaseRequisitionCommandHandler
  - RequesterUserId = Diana.UserId
  - Save to DB (status=Draft)
        ↓
Diana submits requisition
        ↓
SubmitPurchaseRequisitionCommand
        ↓
SubmitPurchaseRequisitionCommandHandler
  - Transition to AwaitingApproval
  - Save to DB
        ↓
[ENDPOINT 1 - Diana attempts to approve (SoD violation)]
        ↓
ApprovePurchaseRequisitionCommand
  - ApprovedByUserId = Diana.UserId
        ↓
ApprovePurchaseRequisitionCommandHandler
  - Call SodPolicy.ValidateAsync()
    - Check: RequesterUserId (Diana) ≠ ApprovedByUserId (Diana)
    - Returns: SoD violation error
  - Return error: "Cannot approve own requisition"
        ↓
Response: 403 Forbidden
        ↓
[ENDPOINT 2 - Eve (Manager) delegates authority to Bob (Deputy)]
        ↓
API: POST /authorization/delegations
  - DelegateFromUserId = Eve.UserId
  - DelegateToUserId = Bob.UserId
  - Grants = ["purchasing:approve-requisitions"]
  - StartDate = 2025-08-15
  - EndDate = 2025-08-29
        ↓
DelegationStore saves delegation record
        ↓
[ENDPOINT 3 - Bob (Deputy) approves requisition (delegated authority)]
        ↓
ApprovePurchaseRequisitionCommand
  - ApprovedByUserId = Bob.UserId
        ↓
ApprovePurchaseRequisitionCommandHandler
  - Call SodPolicy.ValidateAsync()
    - Check: RequesterUserId (Diana) ≠ ApprovedByUserId (Bob) ✓
    - Check: Does Bob have grant "purchasing:approve-requisitions"?
      - Resolve via DelegationAwarePermissionResolver
      - Bob has delegation from Eve for that grant (within date range)
      - Returns: TRUE
    - SoD validation passes
  - Transition to Approved
  - ApprovedByUserId = Bob.UserId
  - Save to DB
  - Audit log: action=Approved, userId=Bob, delegatedFrom=Eve
        ↓
Response: 200 OK
        ↓
CreatePurchaseOrderCommand (based on approved requisition)
        ↓
Order workflow proceeds
```

---

## Technology Stack

- **Framework**: Modulus (.NET 10)
- **ORM**: Entity Framework Core 10.x
- **CQRS**: Modulus.Mediator (wrapper around MediatR)
- **Sagas**: Modulus.Sagas (based on Rebus sagas)
- **Message Bus**: Rebus + RabbitMQ
- **Outbox/Inbox**: Modulus.Outbox
- **Background Jobs**: Quartz.NET
- **Database**: PostgreSQL (schema-per-module)
- **Cache**: Redis
- **Search**: (optional) Elasticsearch
- **Storage**: MinIO (S3-compatible)
- **Auth**: OpenIddict (native Modulus Identity module)
- **Field Security**: Modulus.Authorization.FieldSecurity
- **Encryption**: `IPersonalDataProtector` (Modulus.Security)

---

## Key Architectural Decisions

1. **Schema-per-module**: Each module owns one Postgres schema. No cross-schema foreign keys. Enables independent scaling and schema versioning.

2. **CQRS**: Separate read/write paths. Commands use repositories; queries use SQL (Dapper or EF directly). Allows read optimization without affecting write consistency.

3. **Event-driven**: Domain events → Integration events → Outbox → RabbitMQ → Inbox. Guarantees delivery. Decouples modules temporally.

4. **Saga for workflows**: Multi-step business processes (order fulfillment) modeled as sagas with compensation paths. Handles failure elegantly.

5. **Value Objects for domain concepts**: Money, Quantity, Email are immutable, validated at creation. Prevents invalid states at compile time.

6. **[Classified] for security**: Fields marked `[Classified]` are hidden from unauthorized roles. No branching in handlers — declarative and centralized.

7. **Specification pattern for queries**: Complex WHERE logic encapsulated in reusable Specification classes. Promotes consistency and testability.

8. **PublicApi for sync reads**: Modules can inject `I{Module}PublicApi` for immediate reads (e.g., validate customer exists). Restricted to `.Contracts` projects only.

9. **Org scope for hierarchy**: `IHasOrgUnit` + `ICurrentDataScope` enforce hierarchy-based filtering at query level. No memory filtering — all filtering in SQL WHERE clause.

10. **Feature entitlements for upsell**: `IFeatureEntitlementResolver` gates features by plan. No code branching — declarative via `FeatureGate` attribute.

