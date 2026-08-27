# ModulusSampleErp Implementation Plan

## 1. Executive Summary

Transform ModulusSampleErp from a platform-focused sample (7 infrastructure modules) into a **complete B2B wholesale distribution ERP** that demonstrates all Modulus framework differentiators.

**What**: Add 6 business modules (Catalog, Partners, Inventory, Sales, Purchasing, Billing) that showcase real enterprise complexity.

**Why**: Current sample shows only CRUD over configuration records. New modules demonstrate:
- Sagas with compensation (order fulfillment)
- Segregation of duties (requisition approval)
- Field-level security (product margins)
- Org scope + data scope (hierarchy-based filtering)
- Feature entitlements (multi-warehouse plans)

**Timeline**: 3 phases over ~8 weeks. Each phase is independently valuable; Phase 1 alone (Catalog, Partners, Inventory, Sales) is a complete demo.

---

## 2. Architecture & Patterns

### 2.1 Architectural Parity with Reference Blueprint

ModulusSampleErp already implements the same patterns as the user's reference blueprint — Modulus provides them natively:

| Pattern | User's Blueprint | ModulusSampleErp |
|---------|------------------|------------------|
| Result pattern | Hand-rolled `Result<T>` | `Modulus.Shared.Domain.Result<T>` |
| Aggregates + events | Hand-rolled `AggregateRoot` | `Modulus.Domain.AggregateRoot<TId>` |
| CQRS | MediatR | `Modulus.Mediator` |
| Outbox/Inbox | Quartz + Rebus + SQL polling | `Modulus.Outbox` + `Modulus.Sagas` |
| Endpoints | Minimal API + `TypedResults` | `Endpoint<TRequest,TResponse>` (auto-discovered) |
| Message bus | Rebus | EventBus (RabbitMQ via Modulus integration) |
| Schema-per-module | Manual SQL | Docker Compose + EF Core per-schema migrations |

**Key difference**: Modulus abstracts the hand-rolled infrastructure; the domain DDD patterns remain identical.

### 2.2 Shared Kernel Patterns

All business modules depend on `ModulusSample.Shared.Domain` and reuse:

#### Value Objects (Add to Shared.Domain)

Provide validation + immutability. Use `readonly record struct` + `Create()` factory returning `Result<T>`.

```csharp
// Example: Money value object
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0)
            return Result.Failure<Money>(
                Error.Validation("Money.Negative", "Amount cannot be negative"));

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            return Result.Failure<Money>(
                Error.Validation("Money.InvalidCurrency", "Currency must be 3-letter code"));

        return Result.Success(new Money(amount, currency));
    }
}

// Example: Quantity value object
public readonly record struct Quantity(int Value)
{
    public static Result<Quantity> Create(int value)
    {
        if (value <= 0)
            return Result.Failure<Quantity>(
                Error.Validation("Quantity.Invalid", "Quantity must be > 0"));

        return Result.Success(new Quantity(value));
    }
}
```

**Apply to**: Catalog (UnitPrice, CostPrice), Inventory (Quantity, ReorderPoint), Sales (Amount, Discount), Purchasing (Amount), Billing (Amount, TaxAmount).

#### Specification Pattern (Add to Shared.Application)

Encapsulate complex query logic for reuse across multiple handlers. Use when WHERE predicates repeat in 2+ queries.

```csharp
// Base
public abstract class Specification<T> where T : class
{
    public Expression<Func<T, bool>>? Criteria { get; protected set; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public int? PageNumber { get; protected set; }
    public int? PageSize { get; protected set; }
    
    protected void AddInclude(Expression<Func<T, object>> include)
        => Includes.Add(include);
}

// Example: Purchasing approval queue (SoD + delegation)
public class PurchaseRequisitionsAwaitingApprovalSpec : Specification<PurchaseRequisition>
{
    public PurchaseRequisitionsAwaitingApprovalSpec(Guid approverId, OrgUnit? delegatedFromUnit = null)
    {
        // Criteria: awaiting approval in my org unit OR delegated to me
        Criteria = req => req.Status == RequisitionStatus.AwaitingApproval &&
            (req.OrgUnit.Path.StartsWith(approverId) || req.DelegatedToUserId == approverId);
        
        AddInclude(r => r.Lines);
        AddInclude(r => r.Requester);
    }
}

// Usage in handler
public class ListPurchaseRequisitionsAwaitingApprovalQueryHandler :
    IQueryHandler<ListPurchaseRequisitionsAwaitingApprovalQuery, List<RequisitionDto>>
{
    private readonly IPurchaseRequisitionRepository _repo;

    public async Task<Result<List<RequisitionDto>>> Handle(
        ListPurchaseRequisitionsAwaitingApprovalQuery request,
        CancellationToken ct)
    {
        var spec = new PurchaseRequisitionsAwaitingApprovalSpec(request.UserId);
        var requisitions = await _repo.ListBySpecificationAsync(spec, ct);
        return Result.Success(requisitions.ToDto());
    }
}
```

**Apply to**: Purchasing (approval queues), Sales (org-scoped orders).

---

## 3. Module Specifications

### 3.1 Catalog Module

**Purpose**: Demonstrate Redis caching, field-level security, Media integration, search.

**Aggregates**:
- `Product` (AggregateRoot)
  - `ProductId` (Guid)
  - `Sku` (string, unique)
  - `Name`, `Description`
  - `UnitPrice` (Money value object)
  - `CostPrice` (Money, [Classified] — visible only to Finance)
  - `Margin` (computed, [Classified])
  - `Category` (ProductCategory entity reference)
  - `Status` (Active, Discontinued, Draft)
  - `CreatedByUserId`, `TenantId`

- `ProductCategory` (AggregateRoot)
  - `CategoryId` (Guid)
  - `Name`, `Code`
  - `ParentCategoryId` (nullable, supports hierarchy)

- `UnitOfMeasure` (Value Object)
  - `Id` (Guid)
  - `Code` (PCS, BOX, KG, etc.)
  - `Name`

**Key Features**:
- Redis caching with tag-based invalidation (get product → cache miss → query DB → cache hit for 1hr; update product → invalidate `product:*` tag)
- `[Classified]` on CostPrice/Margin — same endpoint returns different JSON to Finance vs Sales
- Media module integration — `GET /products/{id}` includes image URLs from Media module

**Endpoints** (REPR pattern):
- `POST /api/catalog/products` — Create product
- `GET /api/catalog/products/{id}` — Get single product (with field security)
- `GET /api/catalog/products?skip=0&take=20` — List products (paginated)
- `PUT /api/catalog/products/{id}` — Update product
- `GET /api/catalog/categories` — List categories

**CQRS Handlers**:
- `CreateProductCommand` → `CreateProductCommandHandler`
- `UpdateProductCommand` → `UpdateProductCommandHandler`
- `GetProductByIdQuery` → `GetProductByIdQueryHandler`
- `SearchProductsQuery` → `SearchProductsQueryHandler` (uses ISearchRepository)

**Domain Events**:
- `ProductCreatedDomainEvent`
- `ProductUpdatedDomainEvent`
- `ProductDiscontinuedDomainEvent`

**Schema**: `catalog_schema`

---

### 3.2 Partners Module

**Purpose**: Demonstrate PII encryption, data scope (owner-based filtering), tenant isolation.

**Aggregates**:
- `Customer` (AggregateRoot)
  - `CustomerId` (Guid)
  - `CompanyName`, `TaxId` (encrypted at rest)
  - `PrimaryContact` (Contact entity, includes Email, Phone — encrypted)
  - `Address` (encrypted)
  - `CreditLimit` (Money value object)
  - `Status` (Active, Inactive, Suspended)
  - `OwnedByUserId` (sales rep — filters via data scope)
  - `TenantId`

- `Supplier` (AggregateRoot)
  - `SupplierId` (Guid)
  - `CompanyName`, `TaxId` (encrypted)
  - `Contact` (Contact entity)
  - `PaymentTerms` (Net 30, etc.)
  - `Status`
  - `TenantId`

- `Contact` (Entity, not aggregate)
  - `ContactId` (Guid)
  - `Email` (encrypted)
  - `Phone` (encrypted)
  - `Name`
  - `Title`

**Key Features**:
- `IPersonalDataProtector` encryption for PII (email, phone, address)
- `IHasOwner` + `ICurrentDataScope` — Sales rep sees only their customers; manager sees team's
- Tenant isolation — each tenant's customers are filtered at the query level

**Endpoints**:
- `POST /api/partners/customers` — Create customer (owned by current user)
- `GET /api/partners/customers/{id}` — Get customer (field-level encryption applied)
- `GET /api/partners/customers?skip=0&take=20` — List my customers (scoped by current user + org unit)
- `PUT /api/partners/customers/{id}` — Update customer
- `POST /api/partners/suppliers` — Create supplier
- `GET /api/partners/suppliers/{id}` — Get supplier

**CQRS**:
- `CreateCustomerCommand` → `CreateCustomerCommandHandler`
- `ListMyCustomersQuery` → `ListMyCustomersQueryHandler` (scoped by `ICurrentDataScope.UserId` + `OrgUnitPath`)
- `GetCustomerByIdQuery` → `GetCustomerByIdQueryHandler` (decrypts PII on demand)

**Domain Events**:
- `CustomerCreatedDomainEvent`
- `CustomerCreditLimitReachedDomainEvent`

**Schema**: `partners_schema`

---

### 3.3 Inventory Module

**Purpose**: Demonstrate org scope, optimistic concurrency, background jobs, SignalR push.

**Aggregates**:
- `Warehouse` (AggregateRoot, IHasOrgUnit)
  - `WarehouseId` (Guid)
  - `Name`, `Location`
  - `OrgUnitId` (Guid) — branch/warehouse belongs to an org unit
  - `Status` (Active, Inactive)
  - `TenantId`

- `StockItem` (AggregateRoot)
  - `StockItemId` (Guid)
  - `ProductId` (Guid, from Catalog)
  - `WarehouseId` (Guid)
  - `AvailableQuantity` (Quantity value object)
  - `ReservedQuantity` (Quantity)
  - `ReorderPoint` (Quantity)
  - `LastCountedUtc` (DateTime)
  - `Version` (int, for optimistic concurrency)
  - `TenantId`

- `StockReservation` (Entity)
  - `ReservationId` (Guid)
  - `StockItemId` (Guid)
  - `QuantityReserved` (Quantity)
  - `ReservationReason` (SalesOrder, PurchaseOrder, etc.)
  - `ReferenceId` (Guid, e.g., SalesOrderId)
  - `Status` (Reserved, Committed, Cancelled)

- `StockMovement` (AggregateRoot, audit trail)
  - `MovementId` (Guid)
  - `StockItemId` (Guid)
  - `MovementType` (In, Out, Adjustment, Transfer)
  - `Quantity` (signed)
  - `Reason`, `DocumentId`
  - `CreatedUtc`, `CreatedByUserId`

**Key Features**:
- `IHasOrgUnit` on Warehouse — when listing warehouses, filter by current user's org unit
- Optimistic concurrency on StockItem via `Version` — handles concurrent stock updates (two shipments reduce available qty)
- Background job (Quartz) — check reorder points every 30min, publish `LowStockWarningEvent`
- SignalR hub — warehouse managers subscribe to low-stock alerts in real-time

**Endpoints**:
- `POST /api/warehouses` — Create warehouse
- `GET /api/warehouses?skip=0&take=20` — List warehouses (filtered by org scope)
- `GET /api/warehouses/{id}/stock-items` — List stock in warehouse
- `GET /api/warehouses/{id}/stock-items/{stockItemId}` — Get stock details
- `POST /api/stock-movements/transfer` — Transfer stock between warehouses

**CQRS**:
- `CreateWarehouseCommand` → `CreateWarehouseCommandHandler`
- `ListWarehousesByOrgUnitQuery` → `ListWarehousesByOrgUnitQueryHandler` (uses `ICurrentDataScope.OrgUnitPath`)
- `ReserveStockCommand` → `ReserveStockCommandHandler` (increments ReservedQuantity, checks Version)
- `ReleaseStockReservationCommand` → `ReleaseStockReservationCommandHandler` (decrement, handles concurrency)

**Domain Events**:
- `StockItemCreatedDomainEvent`
- `StockReservedDomainEvent`
- `StockReleasedDomainEvent`
- `LowStockWarningEvent` (raised by background job)
- `OutOfStockWarningEvent`

**Integration Event Handlers** (subscribes to):
- `SalesOrderCreatedIntegrationEvent` — attempt stock reservation
- `GoodsReceiptConfirmedIntegrationEvent` — increase available qty

**Schema**: `inventory_schema`

---

### 3.4 Sales Module

**Purpose**: Demonstrate sagas with compensation, workflow policy, discount approval.

**Aggregates**:
- `SalesOrder` (AggregateRoot, IHasOrgUnit)
  - `SalesOrderId` (Guid)
  - `CustomerId` (Guid, from Partners)
  - `OrderNumber` (string, unique per tenant)
  - `OrderDate` (DateTime)
  - `Status` (Draft, Submitted, Confirmed, Picked, Shipped, Invoiced, Cancelled)
  - `Lines` (SalesOrderLine collection)
  - `Total` (Money, computed)
  - `DiscountAmount` (Money)
  - `DiscountApprovedBy` (Guid, nullable — if discount > 5%, must be approved by manager)
  - `ShipToAddress` (string)
  - `OrgUnitId` (Guid) — sales are scoped to branch
  - `TenantId`, `CreatedByUserId`

- `SalesOrderLine` (Entity)
  - `LineId` (Guid)
  - `ProductId` (Guid)
  - `Quantity` (Quantity)
  - `UnitPrice` (Money)
  - `LineTotal` (Money, computed)

- `Shipment` (AggregateRoot)
  - `ShipmentId` (Guid)
  - `SalesOrderId` (Guid)
  - `Lines` (ShipmentLine collection)
  - `ShippedUtc` (DateTime)
  - `TrackingNumber` (string)
  - `Status` (Pending, Shipped, Delivered)

**Key Features**:
- **Saga (Order Fulfillment)**: Submit order → reserve stock (Inventory) → pick → ship → invoice (Billing)
  - Happy path: all steps succeed, saga completes
  - Unhappy path: stock unavailable → saga compensates (rollback reservation)
- **Workflow policy**: Order transitions Draft → Submitted → Confirmed → Picked → Shipped → Invoiced (no back-stepping)
- **Discount approval**: If `DiscountAmount / Total > 5%`, SoD requires manager approval before confirmation
- `IHasOrgUnit` — branch rep sees only their orders; regional manager sees region's orders; CFO sees all

**Endpoints**:
- `POST /api/sales/orders` — Create order (as Draft, idempotent key in header for retry safety)
- `GET /api/sales/orders/{id}` — Get order details
- `GET /api/sales/orders?skip=0&take=20` — List orders (org-scoped)
- `POST /api/sales/orders/{id}/submit` — Submit for fulfillment (triggers saga)
- `POST /api/sales/orders/{id}/confirm` — Confirm (after discount approval if needed)
- `POST /api/sales/orders/{id}/cancel` — Cancel (triggers compensation)

**CQRS**:
- `CreateSalesOrderCommand` → `CreateSalesOrderCommandHandler`
- `SubmitSalesOrderCommand` → `SubmitSalesOrderCommandHandler` (triggers saga)
- `ListSalesOrdersByOrgUnitQuery` → `ListSalesOrdersByOrgUnitQueryHandler`

**Sagas**:
- `OrderFulfillmentSaga` (implements `ISaga<OrderFulfillmentSagaData>`)
  - Start: `SalesOrderSubmittedDomainEvent` → send `ReserveStockCommand` to Inventory
  - Handle `StockReservedIntegrationEvent` → send `PickOrderCommand` to Inventory
  - Handle `OrderPickedIntegrationEvent` → send `ShipOrderCommand` to Fulfillment (external, or local)
  - Handle `OrderShippedIntegrationEvent` → send `CreateInvoiceCommand` to Billing
  - Handle `InvoiceCreatedIntegrationEvent` → Complete saga, publish `OrderFulfilledIntegrationEvent`
  - Compensate on failure: `ReleaseStockReservationCommand` to Inventory

**Domain Events**:
- `SalesOrderCreatedDomainEvent`
- `SalesOrderSubmittedDomainEvent` (triggers saga)
- `SalesOrderConfirmedDomainEvent`
- `SalesOrderCancelledDomainEvent` (triggers compensation)

**Integration Events**:
- Published: `SalesOrderSubmittedIntegrationEvent`, `OrderFulfilledIntegrationEvent`
- Subscribed: `StockReservedIntegrationEvent`, `OrderPickedIntegrationEvent`, `OrderShippedIntegrationEvent`, `InvoiceCreatedIntegrationEvent`

**Schema**: `sales_schema`

---

### 3.5 Purchasing Module

**Purpose**: Demonstrate SoD, delegation, approval chain, workflow policy.

**Aggregates**:
- `PurchaseRequisition` (AggregateRoot)
  - `RequisitionId` (Guid)
  - `RequisitionNumber` (string, unique)
  - `Status` (Draft, Submitted, AwaitingApproval, Approved, Rejected, Received)
  - `RequesterUserId` (Guid, buyer)
  - `SubmittedUtc` (DateTime, nullable)
  - `ApprovedByUserId` (Guid, nullable, purchasing manager — **SoD: must != RequesterUserId**)
  - `ApprovedUtc` (DateTime, nullable)
  - `Lines` (RequisitionLine collection)
  - `Total` (Money)
  - `OrgUnitId` (Guid) — part of approval hierarchy
  - `TenantId`

- `PurchaseOrder` (AggregateRoot)
  - `OrderId` (Guid)
  - `OrderNumber` (string, unique)
  - `RequisitionId` (Guid, original requisition)
  - `SupplierId` (Guid, from Partners)
  - `OrderDate` (DateTime)
  - `Status` (Draft, Sent, AwaitingReceipt, PartiallyReceived, Received, Cancelled)
  - `Lines` (PurchaseOrderLine collection)
  - `Total` (Money)
  - `TenantId`

- `GoodsReceipt` (AggregateRoot)
  - `ReceiptId` (Guid)
  - `OrderId` (Guid)
  - `ReceiptDate` (DateTime)
  - `Lines` (ReceiptLine collection, with quantities received vs. ordered)
  - `Status` (Pending, Confirmed, Rejected)
  - `ConfirmedByUserId` (Guid, warehouse manager)
  - `ConfirmedUtc` (DateTime, nullable)

**Key Features**:
- **SoD (Segregation of Duties)**: 
  - Buyer raises requisition
  - Purchasing manager (different user) must approve
  - Cannot approve own requisition (framework validates via `SodPolicy`)
- **Delegation**:
  - Purchasing manager on leave delegates approval authority to deputy for 2 weeks
  - Deputy approves requisitions in manager's place
  - Delegation time-bound and logged
- **Approval chain**: Requisition → PO → Receipt (state machine)
- **Workflow policy**: Requisition transitions Draft → Submitted → AwaitingApproval → Approved → Received (no back-stepping)

**Endpoints**:
- `POST /api/purchase-requisitions` — Create requisition (as Buyer role, Draft status)
- `POST /api/purchase-requisitions/{id}/submit` — Submit for approval
- `GET /api/purchase-requisitions?filter=awaiting-my-approval` — My approval queue (uses Specification)
- `POST /api/purchase-requisitions/{id}/approve` — Approve (SoD enforced)
- `POST /api/purchase-requisitions/{id}/reject` — Reject with reason
- `POST /api/purchase-orders` — Create PO from approved requisition
- `POST /api/goods-receipts` — Create goods receipt
- `POST /api/goods-receipts/{id}/confirm` — Confirm receipt (reconciles with PO)

**CQRS**:
- `CreatePurchaseRequisitionCommand` → handler
- `SubmitPurchaseRequisitionCommand` → handler
- `ApprovePurchaseRequisitionCommand` → handler (SoD check: `SodPolicy.ValidateAsync()`)
- `ListRequisitionsAwaitingMyApprovalQuery` → handler (uses `PurchaseRequisitionsAwaitingApprovalSpec`)
- `CreatePurchaseOrderCommand` → handler
- `ConfirmGoodsReceiptCommand` → handler (reconcile received qty with ordered qty)

**Domain Events**:
- `PurchaseRequisitionCreatedDomainEvent`
- `PurchaseRequisitionSubmittedDomainEvent`
- `PurchaseRequisitionApprovedDomainEvent`
- `PurchaseRequisitionRejectedDomainEvent`
- `PurchaseOrderCreatedDomainEvent`
- `GoodsReceiptConfirmedDomainEvent` (triggers stock increase in Inventory)

**Integration Events**:
- Published: `PurchaseRequisitionApprovedIntegrationEvent` → Billing (for accruals)
- Subscribed: None (single-module responsibility)

**Schema**: `purchasing_schema`

---

### 3.6 Billing Module

**Purpose**: Demonstrate field-level security on margins, cross-module event consumption, feature entitlements, background jobs.

**Aggregates**:
- `Invoice` (AggregateRoot)
  - `InvoiceId` (Guid)
  - `InvoiceNumber` (string, unique)
  - `SalesOrderId` (Guid, from Sales)
  - `CustomerId` (Guid, from Partners)
  - `InvoiceDate` (DateTime)
  - `DueDate` (DateTime)
  - `Status` (Draft, Issued, Sent, PartiallyPaid, Paid, Overdue, WriteOff)
  - `Lines` (InvoiceLine collection)
  - `Total` (Money)
  - `AmountPaid` (Money)
  - `TaxAmount` (Money, [Classified] — visible to Finance only)
  - `Margin` (computed from cost, [Classified])
  - `TenantId`

- `InvoiceLine` (Entity)
  - `LineId` (Guid)
  - `ProductId` (Guid)
  - `Quantity` (Quantity)
  - `UnitPrice` (Money)
  - `LineTotal` (Money)
  - `CostPrice` (Money, [Classified]) — for margin calc

- `Payment` (AggregateRoot)
  - `PaymentId` (Guid)
  - `InvoiceId` (Guid)
  - `PaymentDate` (DateTime)
  - `Amount` (Money)
  - `Method` (Check, Wire, CreditCard, etc.)
  - `Status` (Pending, Confirmed, Cleared)
  - `ConfirmedUtc` (DateTime, nullable)

- `CreditNote` (AggregateRoot)
  - `CreditNoteId` (Guid)
  - `CreditNoteNumber` (string)
  - `InvoiceId` (Guid)
  - `Reason` (Returns, Damaged, Discount, etc.)
  - `Amount` (Money)
  - `Status` (Draft, Issued, Applied)
  - `AppliedToPaymentId` (Guid, nullable)

**Key Features**:
- **Field-level security**: `TaxAmount` and `Margin` visible only to Finance role
  - Same `GET /invoices/{id}` endpoint returns different JSON to Sales rep vs Finance
  - Framework's `[Classified]` + `FieldAuthorizer` handles this
- **Cross-module event consumption**:
  - Subscribe to `SalesOrderSubmittedIntegrationEvent` (Sales) → pre-create invoice draft
  - Subscribe to `OrderShippedIntegrationEvent` (Sales) → issue invoice
  - Subscribe to `GoodsReceiptConfirmedIntegrationEvent` (Purchasing) → create payable (not implemented, but placeholder)
- **Feature entitlements**: 
  - "Monthly AR reports" only available on Enterprise plan
  - `IFeatureEntitlementResolver` checks if tenant's plan includes "billing-reports"
- **Background jobs**:
  - `ARAging` job (every hour) — classify invoices as Current, 30-days, 60-days, 90+ days overdue
  - `OutstandingBalanceJob` — daily email to finance on invoices > 60 days overdue
  - `WriteOffEligibilityJob` — flag invoices > 12 months overdue as eligible for write-off

**Endpoints**:
- `POST /api/invoices` — Create invoice (typically from saga, but can be manual)
- `GET /api/invoices/{id}` — Get invoice (field security applied)
- `GET /api/invoices?skip=0&take=20` — List invoices (with aging classification)
- `POST /api/invoices/{id}/send` — Send to customer
- `POST /api/invoices/{id}/mark-as-paid` — Record payment
- `POST /api/payments` — Record payment
- `POST /api/credit-notes` — Create credit note
- `POST /api/credit-notes/{id}/apply` — Apply to invoice
- `GET /api/reports/ar-aging?planFilter=Enterprise` — AR aging report (feature-gated)

**CQRS**:
- `CreateInvoiceCommand` → handler (can be from Sales saga or manual)
- `IssueInvoiceCommand` → handler
- `SendInvoiceCommand` → handler
- `RecordPaymentCommand` → handler
- `GetInvoiceByIdQuery` → handler (applies field security)
- `ListInvoicesQuery` → handler
- `GetArAgingReportQuery` → handler (feature-gated, requires "billing-reports" entitlement)

**Integration Event Handlers**:
- `SalesOrderSubmittedIntegrationEventHandler` → create draft invoice
- `OrderShippedIntegrationEventHandler` → issue invoice
- `OrderFulfilledIntegrationEventHandler` → mark as paid

**Background Jobs** (Quartz):
- `ARAging` — run hourly, update invoice aging classification
- `OutstandingBalanceJob` — run daily, email finance on 60+ days overdue
- `WriteOffEligibilityJob` — run weekly, flag eligible invoices

**Domain Events**:
- `InvoiceCreatedDomainEvent`
- `InvoiceIssuedDomainEvent`
- `PaymentRecordedDomainEvent`

**Schema**: `billing_schema`

---

## 4. Prerequisite Fixes

These must be completed before starting Phase 1. Each unblocks specific scenarios.

### 4.1 Enable Sagas

**File**: `src/API/ModulusSample.Api/Program.cs` (~L399)

**Current**: 
```csharp
// No saga handlers registered
```

**Fix**:
- Uncomment `builder.Services.AddSagas()` if present
- Register saga handler: `builder.Services.AddSagaHandler<OrderFulfillmentSaga>();`
- Ensure `Modulus.Sagas` NuGet package is referenced

**Verification**: 
```bash
dotnet build
# Saga types should compile without error
```

### 4.2 Map Authorization Management Endpoints

**File**: `src/API/ModulusSample.Api/Program.cs` (~L450)

**Current**: 
```csharp
// app.MapModulusAuthorizationManagement(); // parameter inference issue
```

**Fix**:
- Implement parameter binding for `GrantId`, `OrgUnitId`, etc. via minimal API model binding
- Or use `[FromRoute]`, `[FromBody]` attributes if endpoints are already defined
- Uncomment and test `MapModulusAuthorizationManagement()`

**Verification**:
```bash
# Should have endpoints like:
# POST /authorization/grants
# POST /authorization/delegations
# GET /authorization/delegations?userId=...
```

### 4.3 Seed Believable Demo Data

**File**: `src/API/ModulusSample.Api/Extensions/SampleDataSeeder.cs`

**Current**: 4 settings, placeholder data, all `TenantId = Guid.Empty`

**Required Seed Data**:

#### Tenants (2)
```
- Acme Corp (TenantId = <guid1>, active plan = Enterprise)
- StartUp Inc (TenantId = <guid2>, active plan = Starter)
```

#### Org Tree (1 Company, 2 Regions, 4 Branches/Warehouses)
```
Company (root, OrgUnitId=<guid>, OrgUnitPath="/c001/")
├── North Region (OrgUnitId=<guid>, OrgUnitPath="/c001/r001/")
│   ├── NYC Warehouse (OrgUnitId=<guid>, OrgUnitPath="/c001/r001/w001/")
│   ├── Boston Warehouse (OrgUnitId=<guid>, OrgUnitPath="/c001/r001/w002/")
└── South Region (OrgUnitId=<guid>, OrgUnitPath="/c001/r002/")
    ├── Miami Warehouse (OrgUnitId=<guid>, OrgUnitPath="/c001/r002/w003/")
    ├── Atlanta Warehouse (OrgUnitId=<guid>, OrgUnitPath="/c001/r002/w004/")
```

#### Users & Roles (6 personas, grant-based permissions)

| User | Role | Org Unit | Grants | Scenario |
|------|------|----------|--------|----------|
| alice@acme | Sales Rep | NYC Warehouse | `sales:create-order`, `sales:view-own-orders`, `catalog:view-products` | Scenario 1, 4 |
| bob@acme | Branch Manager | NYC Warehouse | `sales:view-org-orders`, `inventory:view-org-stock`, `purchasing:view-requisitions` | Scenario 4 |
| charlie@acme | Regional Manager | North Region | `sales:view-region-orders`, `inventory:view-region-stock`, `purchasing:approve-requisitions` | Scenario 4 |
| diana@acme | Buyer | North Region | `purchasing:create-requisition`, `catalog:view-products` | Scenario 2 |
| eve@acme | Purchasing Manager | Company | `purchasing:approve-requisitions`, `purchasing:delegate-authority`, `purchasing:view-all-requisitions` | Scenario 2 |
| frank@acme | Finance | Company | `billing:view-invoices`, `catalog:view-cost-price`, `catalog:view-margin`, `billing:view-tax-amount`, `billing:access-reports` | Scenario 3, 5 |

#### Sample Data
- 10 Products (various categories, with cost/sell prices)
- 4 Suppliers (with payment terms)
- 6 Customers (across regions, with credit limits)
- 3 Warehouses (with stock levels, reorder points)

**Implementation**: Update `SampleDataSeeder.cs` to call module seeders:
```csharp
public async Task SeedAsync()
{
    await SeedTenantsAsync();
    await SeedOrgUnitsAsync();
    await SeedUsersAndGrantsAsync();
    await SeedCatalogAsync();
    await SeedPartnersAsync();
    await SeedInventoryAsync();
}
```

### 4.4 Normalize Media Module

**File**: `src/Modules/Media/ModulusSample.Modules.Media.Presentation/`

**Current**: `FilesController.cs`, `FoldersController.cs` (MVC controllers)

**Fix**:
- Delete MVC controllers
- Convert all endpoints to `Endpoint<TRequest, TResponse>` classes
- Place in `Folders/` and `Files/` subdirectories (feature-based organization)
- Example: `CreateMediaFolderEndpoint.cs`, `UploadMediaFileEndpoint.cs`, etc.

**Remaining endpoints to convert**:
- Folder: Create, Read (GetById, ListFolders, GetFolderContents), Update, Delete
- File: Upload, Read (GetById, GetFileByPath, DownloadFile), Delete

**Schema**: `media_schema` (already correct)

### 4.5 Clean Up Hospital Sample Leftovers

**Files to delete**:
- `src/Modules/Identity/Domain/Events/Hospital/` (directory)
- `docs/TESTING.md` — remove `hospital` schema reference if present
- Root logs: `app-error.log`, `app-out.log`, `app-output.log`, `app_err.log`, `app_start.log`
- `docker/` (empty directory)

---

## 5. Implementation Phases

### Phase 1: The Spine (Catalog, Partners, Inventory, Sales)

**Delivers scenarios**: 1 (Order-to-cash saga), 3 (Field security), 4 (Org scope)

**Duration**: ~3 weeks

**Dependencies**: All prerequisite fixes

**Sequence**:
1. **Catalog** (week 1)
   - Aggregates: `Product`, `ProductCategory`, `UnitOfMeasure`
   - Value objects: `Money`, `Quantity`
   - CQRS: Create, Read, List, Update
   - Endpoints: REPR pattern
   - Redis caching (tag-based invalidation)
   - Field security: `[Classified]` on cost/margin (test only; no external calls yet)

2. **Partners** (week 1-2, parallel with Catalog)
   - Aggregates: `Customer`, `Supplier`, `Contact`
   - Value objects: `Email`, `PhoneNumber`
   - Encryption: `IPersonalDataProtector` on PII
   - Data scope: `IHasOwner`, `ICurrentDataScope` integration
   - CQRS: Create, Read (my items), List (my items)
   - Endpoints: REPR pattern

3. **Inventory** (week 2)
   - Aggregates: `Warehouse`, `StockItem`, `StockReservation`, `StockMovement`
   - Org scope: `IHasOrgUnit` on Warehouse
   - Optimistic concurrency: `Version` on StockItem
   - Integration event handlers: subscribe to `SalesOrderCreatedIntegrationEvent` for reservation
   - Quartz job: ReorderPointCheck (no-op alert for now, SignalR optional)
   - Endpoints: REPR pattern

4. **Sales** (week 3)
   - Aggregates: `SalesOrder`, `SalesOrderLine`, `Shipment`
   - Value objects: `Money`, `Quantity`
   - Workflow policy: State machine (Draft → Submitted → Confirmed → Picked → Shipped → Invoiced)
   - **Saga (OrderFulfillmentSaga)**: 
     - Trigger: `SalesOrderSubmittedDomainEvent`
     - Steps: ReserveStock (Inventory) → Pick → Ship → CreateInvoice (Billing)
     - Compensate on error: Release reservation
   - Discount approval: Workflow transition validation
   - Org scope: Sales are scoped to branch
   - Endpoints: REPR pattern
   - Integration events: Publish `SalesOrderSubmittedIntegrationEvent`, subscribe to Inventory/Billing events

**Deliverables**:
- ✅ 4 modules scaffolded, domain modeled, CQRS implemented
- ✅ 30+ endpoints (REPR pattern)
- ✅ Saga with compensation working end-to-end
- ✅ Field security working (Catalog)
- ✅ Org scope working (Inventory, Sales)
- ✅ Value objects used throughout
- ✅ Integration tests for each scenario

**Test Coverage**:
- Unit tests: Aggregates (state machines, invariants)
- Integration tests: CQRS handlers, sagas, event handlers
- End-to-end test: Order submission → stock reservation → invoice creation

### Phase 2: Governance (Purchasing)

**Delivers scenario**: 2 (SoD + delegation)

**Duration**: ~2 weeks

**Dependencies**: Phase 1 complete, prerequisite fixes complete

**Implementation**:
1. **Purchasing Module** (week 4-5)
   - Aggregates: `PurchaseRequisition`, `PurchaseOrder`, `GoodsReceipt`
   - SoD: `SodPolicy` validation (requester ≠ approver)
   - Delegation: `IDelegationStore` time-bound delegation of approval authority
   - Approval chain: Workflow transitions + resource policy
   - Specification pattern: `PurchaseRequisitionsAwaitingApprovalSpec` for approval queue
   - Integration event handlers: Subscribe to `OrderFulfilledIntegrationEvent` for accruals (placeholder)
   - Endpoints: REPR pattern (20+)
   - Grant administration: Runtime grant/delegation APIs

**Deliverables**:
- ✅ Purchasing module complete
- ✅ SoD enforced (requisition approval rejects if requester == approver)
- ✅ Delegation working (delegate authority, deputy approves in manager's place)
- ✅ Approval queue endpoint working (uses Specification pattern)
- ✅ Integration tests for SoD, delegation, approval chain

**Test Coverage**:
- SoD rejection test: Create requisition as buyer, attempt approval as same user → denied
- Delegation test: Manager delegates to deputy, deputy approves requisition, audit trail recorded
- Approval queue test: Approval endpoint lists only requisitions pending current user's approval

### Phase 3: Money (Billing)

**Delivers scenario**: 5 (Feature entitlements), plus AR-aging job

**Duration**: ~2 weeks

**Dependencies**: Phase 1 & 2 complete

**Implementation**:
1. **Billing Module** (week 6-7)
   - Aggregates: `Invoice`, `Payment`, `CreditNote`
   - Field security: `[Classified]` on TaxAmount, Margin (test in Catalog module)
   - Integration event handlers: Subscribe to Sales/Purchasing events
   - Cross-module calculation: Margin = (UnitPrice - CostPrice) / UnitPrice (pulls from Catalog)
   - Feature entitlements: AR-aging report only on Enterprise plan
   - Quartz jobs:
     - `ARAging` (hourly) — classify overdue invoices
     - `OutstandingBalanceJob` (daily) — email finance
     - `WriteOffEligibilityJob` (weekly) — flag 12+ month overdue
   - Endpoints: REPR pattern (15+)

**Deliverables**:
- ✅ Billing module complete
- ✅ Field security tested (same invoice endpoint, different JSON for Sales vs Finance)
- ✅ Cross-module integration working (invoice pulls product cost from Catalog)
- ✅ Feature entitlements enforced (AR-aging denied on Starter plan)
- ✅ Background jobs running (Quartz logs verified)
- ✅ Integration tests for event handlers, cross-module flow, feature gates

**Test Coverage**:
- Field security: GET invoice as Sales rep (margins masked) vs Finance (margins visible)
- Feature entitlement: GET AR-aging report on Starter plan → denied; on Enterprise → allowed
- Cross-module: Billing event handler consumes Sales event, creates invoice
- Background job: ARAging job runs, updates aging classification

---

## 6. Demo Scenarios & FEATURE-TOUR.md

Each scenario becomes a section of `docs/FEATURE-TOUR.md` with curl commands and framework pointers.

### Scenario 1: Order-to-Cash Saga

**What it shows**: `Modulus.Sagas`, outbox, cross-module event flow, compensation.

**Happy path**:
```bash
# 1. Create order
curl -X POST http://localhost:8080/api/sales/orders \
  -H "Authorization: Bearer $(get_token alice@acme)" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "<customer-id>",
    "lines": [{"productId": "<prod-id>", "quantity": 5, "unitPrice": 100}],
    "shippingAddress": "123 Main St"
  }'
# Response: 201 Created, orderId=<guid>

# 2. Submit order (triggers saga)
curl -X POST http://localhost:8080/api/sales/orders/<orderId>/submit \
  -H "Authorization: Bearer $(get_token alice@acme)"
# Response: 200 OK
# Behind the scenes:
#   - Sales publishes SalesOrderSubmittedIntegrationEvent
#   - Saga starts: OrderFulfillmentSaga
#   - Saga sends ReserveStockCommand to Inventory
#   - Inventory reserves stock, publishes StockReservedIntegrationEvent
#   - Saga receives event, sends PickOrderCommand
#   - ... continues until InvoiceCreatedIntegrationEvent
#   - Saga completes

# 3. Poll invoice creation
curl -X GET http://localhost:8080/api/billing/invoices \
  -H "Authorization: Bearer $(get_token frank@acme)" \
  -H "Accept: application/json"
# Response: List includes new invoice (created by saga)
```

**Unhappy path** (stock unavailable):
```bash
# 1-2. Same as above, but stock unavailable for one line

# 3. Check order status
curl -X GET http://localhost:8080/api/sales/orders/<orderId> \
  -H "Authorization: Bearer $(get_token alice@acme)"
# Response: status="Cancelled", reason="Insufficient stock: Product ABC (need 5, have 3)"
# Behind the scenes:
#   - Saga step fails (ReserveStockCommand returns error)
#   - Saga compensates: ReleaseStockReservationCommand (if any reservation was made)
#   - Saga transitions order to Cancelled, publishes OrderCancelledIntegrationEvent
```

**Framework pointer**: `src/Modules/Sales/ModulusSample.Modules.Sales.Application/Sagas/OrderFulfillmentSaga.cs`

### Scenario 2: Segregation of Duties + Delegation

**What it shows**: `SodPolicy`, `IDelegationStore`, approval authority.

**Happy path**:
```bash
# 1. Buyer creates requisition
curl -X POST http://localhost:8080/api/purchase-requisitions \
  -H "Authorization: Bearer $(get_token diana@acme)" \
  -H "Content-Type: application/json" \
  -d '{
    "lines": [{"productId": "<prod-id>", "quantity": 100, "unitPrice": 50}]
  }'
# Response: 201 Created, requisitionId=<guid>

# 2. Submit for approval
curl -X POST http://localhost:8080/api/purchase-requisitions/<reqId>/submit \
  -H "Authorization: Bearer $(get_token diana@acme)"
# Response: 200 OK, status="AwaitingApproval"

# 3. Buyer attempts to approve (SoD violation)
curl -X POST http://localhost:8080/api/purchase-requisitions/<reqId>/approve \
  -H "Authorization: Bearer $(get_token diana@acme)"
# Response: 403 Forbidden
# Body: { "error": "SoD violation", "message": "Cannot approve own requisition" }
# Framework validates: requester != approver

# 4. Manager approves
curl -X POST http://localhost:8080/api/purchase-requisitions/<reqId>/approve \
  -H "Authorization: Bearer $(get_token eve@acme)"
# Response: 200 OK, status="Approved", approvedByUserId="eve@acme"

# 5. Create PO from approved requisition
curl -X POST http://localhost:8080/api/purchase-orders \
  -H "Authorization: Bearer $(get_token eve@acme)" \
  -H "Content-Type: application/json" \
  -d '{"requisitionId": "<reqId>", "supplierId": "<supp-id>"}'
# Response: 201 Created, orderId=<guid>
```

**Delegation flow**:
```bash
# 1. Manager creates delegation (manager going on leave)
curl -X POST http://localhost:8080/authorization/delegations \
  -H "Authorization: Bearer $(get_token eve@acme)" \
  -H "Content-Type: application/json" \
  -d '{
    "delegateToUserId": "<deputy-id>",
    "grants": ["purchasing:approve-requisitions"],
    "startDate": "2025-08-15",
    "endDate": "2025-08-29",
    "reason": "On leave"
  }'
# Response: 201 Created, delegationId=<guid>

# 2. Deputy attempts to approve requisition (inherits authority via delegation)
curl -X POST http://localhost:8080/api/purchase-requisitions/<reqId>/approve \
  -H "Authorization: Bearer $(get_token deputy@acme)"
# Response: 200 OK
# Framework resolves: deputy has grant via delegation, validates SoD, approves

# 3. Audit log
curl -X GET http://localhost:8080/authorization/audit-log?resourceId=<reqId> \
  -H "Authorization: Bearer $(get_token eve@acme)"
# Response: [
#   { "action": "Approved", "userId": "eve@acme", "timestamp": "...", "delegatedFrom": null },
#   { "action": "Approved", "userId": "deputy@acme", "timestamp": "...", "delegatedFrom": "eve@acme" }
# ]
```

**Framework pointer**: `src/Modules/Purchasing/ModulusSample.Modules.Purchasing.Application/Commands/ApprovePurchaseRequisitionCommandHandler.cs` (SoD check), `Modulus.Authorization.Delegation` (delegation resolution)

### Scenario 3: Field-Level Security

**What it shows**: `[Classified]`, `FieldAuthorizer`, role-based field masking.

```bash
# 1. Sales rep views product (cost/margin masked)
curl -X GET http://localhost:8080/api/catalog/products/<prod-id> \
  -H "Authorization: Bearer $(get_token alice@acme)"
# Response: 200 OK
# {
#   "productId": "<guid>",
#   "sku": "WIDGET-001",
#   "name": "Acme Widget",
#   "unitPrice": 150,
#   "costPrice": null,  // MASKED
#   "margin": null      // MASKED
# }

# 2. Finance views same product (cost/margin visible)
curl -X GET http://localhost:8080/api/catalog/products/<prod-id> \
  -H "Authorization: Bearer $(get_token frank@acme)"
# Response: 200 OK
# {
#   "productId": "<guid>",
#   "sku": "WIDGET-001",
#   "name": "Acme Widget",
#   "unitPrice": 150,
#   "costPrice": 100,    // VISIBLE
#   "margin": 0.33       // VISIBLE (0.33 = (150-100)/150)
# }

# Same endpoint, same ID, different payloads based on user role.
# No branching in the handler — field masking is declarative via [Classified].
```

**Framework pointer**: `Modulus.Authorization.FieldSecurity`, `[Classified]` attribute, `FieldAuthorizer`

### Scenario 4: Org Scope & Data Scope

**What it shows**: `IHasOrgUnit`, `ICurrentDataScope`, query-level filtering (no memory filtering).

```bash
# Org tree:
# Company (root)
#   ├── North Region (org-path="/co/north/")
#   │   └── NYC Warehouse (org-path="/co/north/nyc/")
#   └── South Region (org-path="/co/south/")
#       └── Miami Warehouse (org-path="/co/south/miami/")

# 1. NYC rep views sales orders in their warehouse
curl -X GET http://localhost:8080/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $(get_token alice@acme)" \
  -H "X-OrgUnit-Path: /co/north/nyc/"
# Response: [orders created by alice or her peers in NYC warehouse]
# Query filter: WHERE orders.OrgUnitPath LIKE '/co/north/nyc%'

# 2. Regional manager views North Region's orders
curl -X GET http://localhost:8080/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $(get_token charlie@acme)" \
  -H "X-OrgUnit-Path: /co/north/"
# Response: [all orders in North Region, including NYC and any sibling branches]
# Query filter: WHERE orders.OrgUnitPath LIKE '/co/north/%'

# 3. CFO views all orders (no org scope)
curl -X GET http://localhost:8080/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $(get_token frank@acme)"
# Response: [all orders across all regions]
# Query filter: none (Finance has global view)

# All three calls hit the same endpoint, same handler code.
# Filtering is applied via ModuleDbContext's query filter (ICurrentDataScope).
```

**Framework pointer**: `IHasOrgUnit` interface, `ICurrentDataScope`, `ModuleDbContext.OnModelCreating()` query filters

### Scenario 5: Tenant Plan Entitlements

**What it shows**: `IFeatureEntitlementResolver`, `FeatureGate` behavior, plan-based access.

```bash
# Tenants:
# - Acme Corp: Enterprise plan (includes all features)
# - StartUp Inc: Starter plan (limited features)

# 1. StartUp attempts to transfer stock between warehouses
curl -X POST http://localhost:8080/api/inventory/stock-movements/transfer \
  -H "Authorization: Bearer $(get_token startup-buyer@startup)" \
  -H "Content-Type: application/json" \
  -d '{"fromWarehouseId": "<w1>", "toWarehouseId": "<w2>", "productId": "<p1>", "quantity": 100}'
# Response: 403 Forbidden
# Body: {
#   "error": "Feature not available",
#   "message": "Multi-warehouse transfers require Enterprise plan",
#   "requiredPlan": "Enterprise"
# }
# Framework gate: FeatureGate("multi-warehouse-transfers") checks entitlements

# 2. Acme (Enterprise) performs same transfer
curl -X POST http://localhost:8080/api/inventory/stock-movements/transfer \
  -H "Authorization: Bearer $(get_token alice@acme)" \
  -H "Content-Type: application/json" \
  -d '{"fromWarehouseId": "<w1>", "toWarehouseId": "<w2>", "productId": "<p1>", "quantity": 100}'
# Response: 200 OK, movementId=<guid>

# 3. AR-aging report (feature-gated to Finance on Enterprise)
curl -X GET http://localhost:8080/api/billing/reports/ar-aging \
  -H "Authorization: Bearer $(get_token startup-cfo@startup)"
# Response: 403 Forbidden
# Body: { "error": "Feature not available", "message": "AR reports require Enterprise plan" }

curl -X GET http://localhost:8080/api/billing/reports/ar-aging \
  -H "Authorization: Bearer $(get_token frank@acme)"
# Response: 200 OK
# Body: { "current": 50000, "30days": 25000, "60days": 10000, "90plus": 5000, ... }
```

**Framework pointer**: `IFeatureEntitlementResolver`, `FeatureGate` mediator behavior, `Modulus.Features.Entitlements`

---

## 7. Testing Strategy

### Test Structure

```
tests/
├── Modules/
│   ├── Catalog/
│   │   ├── ModulusSample.Modules.Catalog.UnitTests/
│   │   │   ├── Domain/ (aggregates, value objects)
│   │   │   ├── Application/ (CQRS handlers)
│   │   │   └── Presentation/ (endpoints)
│   │   └── ModulusSample.Modules.Catalog.IntegrationTests/
│   │       ├── CreateProductHandlerTests.cs
│   │       ├── GetProductByIdHandlerTests.cs
│   │       └── ProductControllerTests.cs (if needed for endpoint integration)
│   ├── Partners/
│   ├── Inventory/
│   ├── Sales/
│   ├── Purchasing/
│   └── Billing/
└── Shared/
    └── ModulusSample.Test.Shared/
        ├── Fixtures/
        │   ├── ModulusWebAppFactory.cs (base factory for all modules)
        │   ├── SeedDataFixture.cs (tenants, org tree, users)
        │   └── AuthenticationFixture.cs (test tokens)
        └── Builders/ (test data builders)
```

### Testing Patterns

#### Unit Tests: Aggregates

```csharp
public class SalesOrderTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsSuccessResult()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var lines = new[] { new OrderLine(Guid.NewGuid(), Quantity.Create(5).Value, Money.Create(100, "USD").Value) };

        // Act
        var result = SalesOrder.Create(customerId, lines);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Draft, result.Value.Status);
        Assert.NotEmpty(result.Value.DomainEvents);
    }

    [Fact]
    public void Confirm_WhenNotPending_ReturnsBadRequest()
    {
        // Arrange
        var order = SalesOrder.Create(...).Value;
        order.Cancel(); // Change status to Cancelled

        // Act
        var result = order.Confirm();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "Order.NotPending");
    }

    [Fact]
    public void RaisesDomainEvent_WhenOrderSubmitted()
    {
        // Arrange
        var order = SalesOrder.Create(...).Value;

        // Act
        var result = order.Submit();

        // Assert
        var domainEvent = Assert.IsType<SalesOrderSubmittedDomainEvent>(order.DomainEvents.Last());
        Assert.Equal(order.Id, domainEvent.OrderId);
    }
}
```

#### Integration Tests: CQRS Handlers

```csharp
public class CreateSalesOrderHandlerTests : IClassFixture<ModulusWebAppFactory<Program>>
{
    private readonly ModulusWebAppFactory<Program> _factory;
    private readonly IMediator _mediator;

    public CreateSalesOrderHandlerTests(ModulusWebAppFactory<Program> factory)
    {
        _factory = factory;
        _mediator = factory.Services.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_WithValidCommand_CreatesOrder()
    {
        // Arrange
        var command = new CreateSalesOrderCommand
        {
            CustomerId = Guid.NewGuid(),
            Lines = new[] { new CreateOrderLineDto { ProductId = Guid.NewGuid(), Quantity = 5, UnitPrice = 100 } }
        };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task Handle_WithInvalidCustomer_ReturnsBadRequest()
    {
        // Arrange
        var command = new CreateSalesOrderCommand
        {
            CustomerId = Guid.Empty,
            Lines = new[] { new CreateOrderLineDto { ProductId = Guid.NewGuid(), Quantity = 5, UnitPrice = 100 } }
        };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "Customer.Invalid");
    }
}
```

#### End-to-End Tests: Scenarios

```csharp
public class OrderToSagaScenarioTests : IClassFixture<ModulusWebAppFactory<Program>>
{
    private readonly ModulusWebAppFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly IMediator _mediator;

    public OrderToSagaScenarioTests(ModulusWebAppFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _mediator = factory.Services.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task OrderSubmission_ReservesStock_AndCreatesInvoice()
    {
        // Arrange
        var token = _factory.GenerateToken("alice@acme", roles: ["sales:create-order"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var customerId = (await _factory.SeedCustomerAsync()).Id;
        var productId = (await _factory.SeedProductAsync()).Id;

        // Act 1: Create order
        var createResponse = await _client.PostAsJsonAsync("/api/sales/orders", new
        {
            customerId,
            lines = new[] { new { productId, quantity = 5, unitPrice = 100 } }
        });
        var orderId = (await createResponse.Content.ReadAsAsync<dynamic>()).id;

        // Act 2: Submit order (triggers saga)
        var submitResponse = await _client.PostAsync($"/api/sales/orders/{orderId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        // Act 3: Wait for saga completion (poll or use SignalR)
        await Task.Delay(2000); // Simple polling (better: use saga completion event)

        // Assert: Stock reserved in Inventory
        var stockResponse = await _client.GetAsync("/api/warehouses/nyc/stock-items?productId=" + productId);
        var stockData = await stockResponse.Content.ReadAsAsync<dynamic>();
        Assert.Equal(5, stockData.reservedQuantity); // 5 units reserved

        // Assert: Invoice created in Billing
        var invoiceResponse = await _client.GetAsync("/api/billing/invoices?orderid=" + orderId);
        var invoiceData = await invoiceResponse.Content.ReadAsAsync<dynamic>();
        Assert.NotEmpty(invoiceData); // Invoice created
    }

    [Fact]
    public async Task OrderSubmission_WithInsufficientStock_CompensatesAndCancels()
    {
        // Arrange: Product with only 2 units in stock
        var token = _factory.GenerateToken("alice@acme", roles: ["sales:create-order"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var customerId = (await _factory.SeedCustomerAsync()).Id;
        var productId = (await _factory.SeedProductAsync(availableQuantity: 2)).Id; // Only 2 in stock

        // Act: Try to order 5 units
        var createResponse = await _client.PostAsJsonAsync("/api/sales/orders", new
        {
            customerId,
            lines = new[] { new { productId, quantity = 5, unitPrice = 100 } }
        });
        var orderId = (await createResponse.Content.ReadAsAsync<dynamic>()).id;

        var submitResponse = await _client.PostAsync($"/api/sales/orders/{orderId}/submit", null);
        await Task.Delay(2000);

        // Assert: Order cancelled
        var orderResponse = await _client.GetAsync($"/api/sales/orders/{orderId}");
        var orderData = await orderResponse.Content.ReadAsAsync<dynamic>();
        Assert.Equal("Cancelled", orderData.status);
        Assert.Contains("Insufficient stock", orderData.reason);

        // Assert: No stock reserved (compensation executed)
        var stockResponse = await _client.GetAsync("/api/warehouses/nyc/stock-items?productId=" + productId);
        var stockData = await stockResponse.Content.ReadAsAsync<dynamic>();
        Assert.Equal(2, stockData.reservedQuantity); // Should still be 0 (reservation rolled back)
    }
}
```

#### SoD Tests

```csharp
public class SodPolicyTests : IClassFixture<ModulusWebAppFactory<Program>>
{
    private readonly ModulusWebAppFactory<Program> _factory;
    private readonly IMediator _mediator;

    public SodPolicyTests(ModulusWebAppFactory<Program> factory)
    {
        _factory = factory;
        _mediator = factory.Services.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task ApprovePurchaseRequisition_BySameUser_ReturnsSodViolation()
    {
        // Arrange
        var buyer = Guid.NewGuid();
        var requisition = (await _factory.SeedPurchaseRequisitionAsync(requesterUserId: buyer)).Id;

        var command = new ApprovePurchaseRequisitionCommand
        {
            RequisitionId = requisition,
            ApprovedByUserId = buyer // Same user as requester
        };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "SoD.Violation");
    }

    [Fact]
    public async Task ApprovePurchaseRequisition_ByDifferentUser_Succeeds()
    {
        // Arrange
        var buyer = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var requisition = (await _factory.SeedPurchaseRequisitionAsync(requesterUserId: buyer)).Id;

        var command = new ApprovePurchaseRequisitionCommand
        {
            RequisitionId = requisition,
            ApprovedByUserId = manager // Different user
        };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApprovePurchaseRequisition_ByDelegateAuthority_Succeeds()
    {
        // Arrange
        var buyer = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var deputy = Guid.NewGuid();
        var requisition = (await _factory.SeedPurchaseRequisitionAsync(requesterUserId: buyer)).Id;

        // Delegate authority from manager to deputy
        await _factory.SeedDelegationAsync(delegateFromUserId: manager, delegateToUserId: deputy);

        var command = new ApprovePurchaseRequisitionCommand
        {
            RequisitionId = requisition,
            ApprovedByUserId = deputy // Deputy has delegated authority
        };

        // Act
        var result = await _mediator.SendAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
        // Audit trail should show: approver=deputy, delegatedFrom=manager
    }
}
```

### CI/CD Integration

- Run unit tests on every commit
- Run integration tests on PR (against SQLite test database)
- E2E tests on staging (against Postgres test database)
- Coverage report: aim for 80%+ on domain/application layers

---

## 8. Success Criteria & Verification

### Build & Compile
```bash
dotnet build ModulusSampleErp.slnx
# Expected: 0 warnings, 0 errors (TreatWarningsAsErrors is global)
```

### Docker Compose
```bash
docker compose up -d
# Expected: All services healthy
# - postgres: ready
# - redis: ready
# - rabbitmq: ready
# - minio: ready
# - api: listening on :8080
```

### Database Migrations
```bash
dotnet run --project src/API/ModulusSample.Api -- --seed
# Expected:
# - All 6 module schemas created (catalog, partners, inventory, sales, purchasing, billing)
# - Seed data populated (2 tenants, org tree, 6 users, sample products/customers/warehouses)
# - Migrations logged
```

### Endpoints Available
```bash
# Phase 1 endpoints
curl http://localhost:8080/swagger/ # Should list 30+ endpoints from Catalog, Partners, Inventory, Sales

# Phase 2 endpoints
curl http://localhost:8080/swagger/ # Should add 20+ Purchasing endpoints

# Phase 3 endpoints
curl http://localhost:8080/swagger/ # Should add 15+ Billing endpoints
```

### Scenario Walkthroughs

Each scenario runnable from `docs/FEATURE-TOUR.md` curl scripts:

```bash
# Scenario 1: Order-to-cash saga
# - Create order, submit, verify stock reserved, verify invoice created

# Scenario 2: SoD + Delegation
# - Create requisition, verify SoD rejection, verify manager approval, verify delegation

# Scenario 3: Field security
# - View product as Sales (no cost/margin), view as Finance (with cost/margin)

# Scenario 4: Org scope
# - View orders as branch rep (only their branch), regional manager (region), CFO (all)

# Scenario 5: Feature entitlements
# - Attempt multi-warehouse transfer on Starter (denied), on Enterprise (allowed)
# - View AR-aging on Starter (denied), on Enterprise (allowed)
```

### Integration Tests

```bash
dotnet test tests/Modules/Catalog/ --logger "console;verbosity=detailed"
dotnet test tests/Modules/Sales/ --logger "console;verbosity=detailed"
# Etc.
# Expected: All tests pass, sagas complete, events published/consumed
```

### Logs & Monitoring

- Seq (`http://localhost:8080` when API is running, or check docker logs)
- Verify saga execution: search for `OrderFulfillmentSaga` in Seq
- Verify background jobs: search for `ARAging` or `OutstandingBalanceJob`
- Verify event handlers: search for integration event handler logs

---

## 9. File Structure Summary

```
ModulusSampleErp/
├── src/
│   ├── API/
│   │   └── ModulusSample.Api/
│   │       ├── Program.cs (module registration, saga/auth/job setup)
│   │       └── Extensions/
│   │           └── SampleDataSeeder.cs (tenants, org tree, users, sample data)
│   ├── Modules/
│   │   ├── Catalog/
│   │   │   ├── ModulusSample.Modules.Catalog.Domain/
│   │   │   ├── ModulusSample.Modules.Catalog.Application/
│   │   │   ├── ModulusSample.Modules.Catalog.Infrastructure/
│   │   │   └── ModulusSample.Modules.Catalog.Presentation/
│   │   ├── Partners/
│   │   ├── Inventory/
│   │   ├── Sales/
│   │   ├── Purchasing/
│   │   ├── Billing/
│   │   └── (7 platform modules already exist: Identity, Tenants, Settings, Features, VirtualFileExplorer, Notifications, Media)
│   └── Shared/
│       ├── ModulusSample.Shared.Domain/ (add Money, Quantity value objects)
│       ├── ModulusSample.Shared.Application/ (add Specification pattern)
│       ├── ModulusSample.Shared.Infrastructure/
│       └── ModulusSample.Shared.Presentation/
├── tests/
│   ├── Modules/
│   │   ├── Catalog/
│   │   ├── Partners/
│   │   ├── Inventory/
│   │   ├── Sales/
│   │   ├── Purchasing/
│   │   └── Billing/
│   └── Shared/
│       └── ModulusSample.Test.Shared/
├── docs/
│   ├── IMPLEMENTATION_PLAN.md (this file)
│   ├── FEATURE-TOUR.md (scenario walkthroughs with curl commands)
│   ├── ARCHITECTURE.md (domain models, UML diagrams)
│   ├── API.md (endpoint reference)
│   └── TESTING.md (test strategy details)
├── docker-compose.yml (already exists, all required services)
├── ModulusSampleErp.slnx
└── (other root files)
```

---

## 10. Implementation Checklist

### Prerequisite Fixes
- [ ] Enable sagas in Program.cs
- [ ] Fix authorization management endpoint binding
- [ ] Seed believable demo data (2 tenants, org tree, 6 personas)
- [ ] Normalize Media module (REPR endpoints only)
- [ ] Delete hospital sample leftovers

### Phase 1: Catalog, Partners, Inventory, Sales (Week 1-3)

#### Catalog
- [ ] Domain: `Product`, `ProductCategory`, `UnitOfMeasure`
- [ ] Value objects: `Money`, `Quantity`
- [ ] Application: CQRS handlers (Create, Read, List, Update)
- [ ] Infrastructure: Repository, DbContext, migrations
- [ ] Presentation: REPR endpoints (5 endpoints)
- [ ] Redis caching with tag invalidation
- [ ] Field security: `[Classified]` on cost/margin (code-ready, no external calls yet)
- [ ] Unit tests: Aggregates
- [ ] Integration tests: CQRS handlers

#### Partners
- [ ] Domain: `Customer`, `Supplier`, `Contact`
- [ ] Value objects: `Email`, `PhoneNumber` (optional)
- [ ] Application: CQRS handlers (Create, Read, List)
- [ ] Infrastructure: Encryption via `IPersonalDataProtector`
- [ ] Infrastructure: Data scope via `IHasOwner`, `ICurrentDataScope`
- [ ] Presentation: REPR endpoints (6 endpoints)
- [ ] Unit tests: Aggregates, value objects
- [ ] Integration tests: Data scope filtering

#### Inventory
- [ ] Domain: `Warehouse`, `StockItem`, `StockReservation`, `StockMovement`
- [ ] Application: CQRS handlers (Reserve, Release, List)
- [ ] Infrastructure: Org scope via `IHasOrgUnit`
- [ ] Infrastructure: Optimistic concurrency on `StockItem.Version`
- [ ] Application: Integration event handlers (subscribe to `SalesOrderCreatedIntegrationEvent`)
- [ ] Infrastructure: Quartz job stub (ReorderPointCheck, no-op for now)
- [ ] Presentation: REPR endpoints (5 endpoints)
- [ ] Unit tests: Aggregates, concurrency
- [ ] Integration tests: Event handlers, org scope

#### Sales
- [ ] Domain: `SalesOrder`, `SalesOrderLine`, `Shipment`
- [ ] Domain: Workflow policy (Draft → Submitted → Confirmed → Picked → Shipped → Invoiced)
- [ ] Application: CQRS handlers (Create, Submit, Confirm, List)
- [ ] Application: **Saga** (`OrderFulfillmentSaga` with compensation)
- [ ] Infrastructure: Integration event publishing
- [ ] Presentation: REPR endpoints (6 endpoints)
- [ ] Unit tests: Aggregates, state machine, saga logic
- [ ] Integration tests: Saga happy path, compensation, org scope

### Phase 2: Purchasing (Week 4-5)

- [ ] Domain: `PurchaseRequisition`, `PurchaseOrder`, `GoodsReceipt`
- [ ] Domain: Workflow policy (Draft → Submitted → AwaitingApproval → Approved → Received)
- [ ] Application: SoD validation via `SodPolicy`
- [ ] Application: Delegation resolution via `IDelegationStore`
- [ ] Application: Specification pattern for approval queue
- [ ] Application: CQRS handlers (Create, Submit, Approve, List, CreatePO, ConfirmReceipt)
- [ ] Infrastructure: Integration event publishing
- [ ] Presentation: REPR endpoints (8+ endpoints)
- [ ] Unit tests: Aggregates, SoD validation, delegation
- [ ] Integration tests: SoD enforcement, delegation, approval queue

### Phase 3: Billing (Week 6-7)

- [ ] Domain: `Invoice`, `Payment`, `CreditNote`
- [ ] Application: Integration event handlers (subscribe to Sales/Purchasing events)
- [ ] Application: Cross-module calculation (Margin from Catalog)
- [ ] Application: Feature entitlement checks
- [ ] Application: CQRS handlers (Create, Issue, Send, Record Payment, CreateCreditNote)
- [ ] Infrastructure: Quartz jobs (ARAging, OutstandingBalance, WriteOffEligibility)
- [ ] Presentation: REPR endpoints (8+ endpoints)
- [ ] Field security: `[Classified]` on TaxAmount, Margin (test with Catalog)
- [ ] Feature gates: AR-aging report
- [ ] Unit tests: Aggregates, cross-module logic
- [ ] Integration tests: Event handlers, feature gates, background jobs

### Documentation & Scenarios (Week 7-8)

- [ ] Write `FEATURE-TOUR.md` with 5 scenario walkthroughs (curl commands)
- [ ] Write `ARCHITECTURE.md` with domain models and diagrams
- [ ] Write `API.md` with endpoint reference
- [ ] Write `TESTING.md` with test execution instructions
- [ ] Record demo video (optional but powerful)
- [ ] Create Postman collection with scenario folders

### Final Verification

- [ ] `dotnet build ModulusSampleErp.slnx` — 0 warnings, 0 errors
- [ ] `docker compose up -d` — all services healthy
- [ ] `dotnet run --project src/API/ModulusSample.Api -- --seed` — migrations run, data seeded
- [ ] All 5 scenarios runnable via `FEATURE-TOUR.md` curl commands
- [ ] Integration tests pass: `dotnet test tests/`
- [ ] Swagger docs show all 50+ endpoints
- [ ] Seq logs show saga/job execution

---

## 11. Key Contacts & References

- **Modulus Framework**: https://github.com/modulus-framework/
- **Reference Blueprint**: (from user's prior project)
- **Modulus Docs**: Check `PROJECT_STRUCTURE.md` for module conventions
- **EF Core**: `DbContext`, migrations, per-schema tracking
- **Quartz.NET**: Background jobs
- **Rebus**: Message bus (RabbitMQ transport)
- **OpenIddict**: OAuth2/OIDC (Identity module)

---

## 12. Appendix: Quick Reference

### Command Line Shortcuts

```bash
# Build & test
dotnet build ModulusSampleErp.slnx
dotnet test tests/

# Run migrations
dotnet ef migrations add {MigrationName} --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/API/ModulusSample.Api
dotnet ef database update --project src/Modules/{Module}/{Module}.Infrastructure --startup-project src/API/ModulusSample.Api

# Run API with seed
dotnet run --project src/API/ModulusSample.Api -- --seed

# Create module (if using modulus CLI)
modulus add-module {ModuleName}

# Docker
docker compose up -d
docker compose logs -f api
docker compose down -v
```

### NuGet Packages to Verify

- `Modulus.Mediator` (CQRS)
- `Modulus.Outbox` (Transactional outbox)
- `Modulus.Sagas` (Sagas)
- `Modulus.Authorization` (SoD, Field security, Delegation)
- `Modulus.Domain` (AggregateRoot, Result)
- `Modulus.Testing` (ModulusWebAppFactory)
- `Rebus` + `Rebus.RabbitMq` (Message bus)
- `Quartz` (Background jobs)
- `EntityFrameworkCore` (ORM)
- `Dapper` (Queries, if used)

---

## End of Implementation Plan

This plan is designed to guide a team through implementing a complete B2B ERP sample that showcases all Modulus framework differentiators. Each phase is independent; start with prerequisites, then Phase 1 (spine), and expand as needed.

For questions or adjustments, refer to the Modulus framework documentation or the reference blueprint.

