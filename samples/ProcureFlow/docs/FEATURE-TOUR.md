# ModulusSampleErp Feature Tour

Complete walkthrough of the five scenarios that demonstrate Modulus framework capabilities. Each scenario is self-contained and executable from scratch.

**Prerequisites**:
```bash
# Start infrastructure
docker compose up -d

# Seed database
dotnet run --project src/API/ModulusSample.Api -- --seed

# Base URL
API_URL=http://localhost:8080
```

---

## Scenario 1: Order-to-Cash Saga

**What it demonstrates**: `Modulus.Sagas`, transactional outbox, cross-module event flow, saga compensation.

**What happens**: Order submitted → stock reserved → picked → shipped → invoiced. Then order with insufficient stock → compensation (rollback).

**Duration**: ~5 minutes

---

### 1.1 Happy Path: Order to Invoice (Stock Available)

#### Step 1: Get credentials

```bash
# Generate token for Alice (Sales Rep)
# Headers will use: Authorization: Bearer $(curl -s http://localhost:8080/auth/token ...)
# For demo, tokens are pre-seeded. Use:

ALICE_TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
# (In real scenario, use OAuth2/OIDC token endpoint)

# Alternatively, test with API key or mocked auth if configured
export TOKEN="test-token-alice"
```

#### Step 2: Create sales order

```bash
curl -X POST $API_URL/api/sales/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: order-123" \
  -d '{
    "customerId": "12345678-1234-1234-1234-123456789012",
    "lines": [
      {
        "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "quantity": 5,
        "unitPrice": 100
      }
    ],
    "shippingAddress": "123 Main St, NYC, NY 10001",
    "shipDate": "2025-08-20"
  }' \
  | jq .

# Response:
# {
#   "orderId": "87654321-4321-4321-4321-210987654321",
#   "orderNumber": "SO-2025-0001",
#   "status": "Draft",
#   "total": 500,
#   "createdAt": "2025-08-13T10:00:00Z"
# }

ORDER_ID="87654321-4321-4321-4321-210987654321"
```

#### Step 3: Submit order (triggers saga)

```bash
curl -X POST $API_URL/api/sales/orders/$ORDER_ID/submit \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' \
  | jq .

# Response:
# {
#   "orderId": "87654321-4321-4321-4321-210987654321",
#   "status": "Submitted",
#   "submittedAt": "2025-08-13T10:00:05Z",
#   "message": "Order submitted for fulfillment. Saga initiated."
# }

# Behind the scenes:
# 1. OrderFulfillmentSaga starts
# 2. Saga sends ReserveStockCommand to Inventory module
# 3. Inventory reserves 5 units, publishes StockReservedIntegrationEvent
# 4. Saga progresses: sends PickOrderCommand
# 5. Saga continues: sends ShipOrderCommand
# 6. Saga reaches final step: sends CreateInvoiceCommand to Billing
# 7. Billing creates invoice, publishes InvoiceCreatedIntegrationEvent
# 8. Saga completes, publishes OrderFulfilledIntegrationEvent
```

#### Step 4: Poll order status (optional)

```bash
curl -X GET $API_URL/api/sales/orders/$ORDER_ID \
  -H "Authorization: Bearer $TOKEN" \
  | jq .

# After ~2-3 seconds (saga completes):
# {
#   "orderId": "87654321-4321-4321-4321-210987654321",
#   "status": "Invoiced",
#   "total": 500,
#   "invoiceId": "11111111-1111-1111-1111-111111111111",
#   "sagaState": "Completed"
# }
```

#### Step 5: Verify stock reserved

```bash
curl -X GET $API_URL/api/inventory/warehouses/nyc/stock-items?productId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa \
  -H "Authorization: Bearer $TOKEN" \
  | jq .

# Response:
# {
#   "warehouseId": "warehouse-nyc-id",
#   "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
#   "availableQuantity": 45,
#   "reservedQuantity": 5,  // <-- Confirms reservation
#   "reorderPoint": 20
# }
```

#### Step 6: Verify invoice created

```bash
curl -X GET $API_URL/api/billing/invoices?orderId=$ORDER_ID \
  -H "Authorization: Bearer $TOKEN" \
  -H "Authorization: Bearer $FRANK_TOKEN" \  # Finance user to see full details
  | jq .

# Response:
# {
#   "invoices": [
#     {
#       "invoiceId": "11111111-1111-1111-1111-111111111111",
#       "invoiceNumber": "INV-2025-0001",
#       "orderId": "87654321-4321-4321-4321-210987654321",
#       "total": 500,
#       "status": "Issued",
#       "dueDate": "2025-09-12",
#       "costPrice": 60,        // VISIBLE to Frank (Finance)
#       "margin": 0.88,         // VISIBLE to Frank (Finance)
#       "createdAt": "2025-08-13T10:00:10Z"
#     }
#   ]
# }
```

**Framework pointer**: 
- Saga: `src/Modules/Sales/ModulusSample.Modules.Sales.Application/Sagas/OrderFulfillmentSaga.cs`
- Outbox processing: `Modulus.Outbox` (automatic via Quartz)
- Integration events: `src/Modules/Sales/ModulusSample.Modules.Sales.IntegrationEvents/`

---

### 1.2 Unhappy Path: Insufficient Stock (Saga Compensation)

#### Setup: Check available stock

```bash
# Product has only 2 units available
curl -X GET $API_URL/api/inventory/warehouses/nyc/stock-items?productId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.availableQuantity'

# Response: 2
```

#### Step 1: Create order for 5 units (more than available)

```bash
curl -X POST $API_URL/api/sales/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "12345678-1234-1234-1234-123456789012",
    "lines": [
      {
        "productId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "quantity": 5,
        "unitPrice": 100
      }
    ],
    "shippingAddress": "123 Main St, NYC, NY 10001"
  }' \
  | jq .

ORDER_ID2="cccccccc-cccc-cccc-cccc-cccccccccccc"
```

#### Step 2: Submit order (saga will fail and compensate)

```bash
curl -X POST $API_URL/api/sales/orders/$ORDER_ID2/submit \
  -H "Authorization: Bearer $TOKEN" \
  -d '{}' \
  | jq .

# Response:
# {
#   "orderId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
#   "status": "Submitted"
# }

# Wait 2-3 seconds for saga to fail...
```

#### Step 3: Check order status (saga compensated and cancelled)

```bash
curl -X GET $API_URL/api/sales/orders/$ORDER_ID2 \
  -H "Authorization: Bearer $TOKEN" \
  | jq .

# Response:
# {
#   "orderId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
#   "status": "Cancelled",
#   "cancelledAt": "2025-08-13T10:05:00Z",
#   "cancelReason": "Insufficient stock: Product XYZ (need 5, have 2)",
#   "sagaState": "CompensatedDueToFailure"
# }

# Saga steps:
# 1. Saga tries to reserve 5 units in Inventory
# 2. ReserveStockCommand fails (only 2 available)
# 3. Saga compensation triggered: ReleaseStockReservationCommand (if partial)
# 4. Order transitioned to Cancelled
# 5. OrderCancelledIntegrationEvent published
```

#### Step 4: Verify stock was NOT reserved

```bash
curl -X GET $API_URL/api/inventory/warehouses/nyc/stock-items?productId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.reservedQuantity'

# Response: 0
# (Proves compensation released reservation)
```

**Framework pointer**: 
- Saga compensation: `src/Modules/Sales/ModulusSample.Modules.Sales.Application/Sagas/OrderFulfillmentSaga.cs` → `CompensateAsync()` method
- Event routing: `Modulus.Outbox` handles delivery even on saga failure

---

## Scenario 2: Segregation of Duties + Delegation

**What it demonstrates**: `SodPolicy` validation, `IDelegationStore`, approval authority.

**What happens**:
1. Buyer tries to approve own requisition → SoD denial
2. Manager approves requisition → success
3. Manager delegates authority to deputy
4. Deputy approves requisition in manager's absence → success with audit trail

**Duration**: ~5 minutes

---

### 2.1 SoD Enforcement: Requester Cannot Approve Own Requisition

#### Step 1: Create requisition as Diana (Buyer)

```bash
DIANA_TOKEN="..."

curl -X POST $API_URL/api/purchase-requisitions \
  -H "Authorization: Bearer $DIANA_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "lines": [
      {
        "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "quantity": 100,
        "unitPrice": 50
      }
    ]
  }' \
  | jq .

# Response:
# {
#   "requisitionId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
#   "requisitionNumber": "PR-2025-0001",
#   "status": "Draft",
#   "requesterUserId": "diana-user-id",
#   "total": 5000
# }

REQ_ID="dddddddd-dddd-dddd-dddd-dddddddddddd"
```

#### Step 2: Submit requisition

```bash
curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID/submit \
  -H "Authorization: Bearer $DIANA_TOKEN" \
  -d '{}' \
  | jq .

# Response:
# {
#   "requisitionId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
#   "status": "AwaitingApproval",
#   "submittedAt": "2025-08-13T10:10:00Z"
# }
```

#### Step 3: Diana attempts to approve (SoD violation) ❌

```bash
curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID/approve \
  -H "Authorization: Bearer $DIANA_TOKEN" \
  -d '{}' \
  | jq .

# Response (403):
# {
#   "error": "SoD_VIOLATION",
#   "message": "Cannot approve own requisition",
#   "code": "SoD.Violation",
#   "details": "Requester and approver must be different users"
# }

# Framework validation: SodPolicy.ValidateAsync()
#   Checks: requisition.RequesterUserId != command.ApprovedByUserId
#   Result: FALSE → rejects approval
```

**Framework pointer**: `Modulus.Authorization.SodPolicy` → `ValidateAsync()`

---

### 2.2 Manager Approval: Different User Can Approve

#### Step 1: Eve (Purchasing Manager) approves ✓

```bash
EVE_TOKEN="..."

curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID/approve \
  -H "Authorization: Bearer $EVE_TOKEN" \
  -d '{}' \
  | jq .

# Response (200):
# {
#   "requisitionId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
#   "status": "Approved",
#   "approvedByUserId": "eve-user-id",
#   "approvedAt": "2025-08-13T10:12:00Z"
# }

# SoD validation passes:
#   Checks: diana-user-id != eve-user-id ✓
#   Proceeds with approval
```

#### Step 2: View audit trail

```bash
curl -X GET $API_URL/authorization/audit-log?resourceType=PurchaseRequisition&resourceId=$REQ_ID \
  -H "Authorization: Bearer $EVE_TOKEN" \
  | jq '.auditLog[]'

# Response:
# [
#   {
#     "action": "Created",
#     "userId": "diana-user-id",
#     "userName": "diana@acme",
#     "timestamp": "2025-08-13T10:10:00Z"
#   },
#   {
#     "action": "Submitted",
#     "userId": "diana-user-id",
#     "timestamp": "2025-08-13T10:10:05Z"
#   },
#   {
#     "action": "Approved",
#     "userId": "eve-user-id",
#     "userName": "eve@acme",
#     "timestamp": "2025-08-13T10:12:00Z",
#     "delegatedFrom": null
#   }
# ]
```

**Framework pointer**: Audit trail logged automatically by `IEnterpriseAuditStore`

---

### 2.3 Delegation: Time-Bounded Authority Transfer

#### Step 1: Eve delegates authority to Bob (Deputy) for 2 weeks

```bash
BOB_USER_ID="bob-user-id"

curl -X POST $API_URL/authorization/delegations \
  -H "Authorization: Bearer $EVE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "delegateToUserId": "'$BOB_USER_ID'",
    "grants": ["purchasing:approve-requisitions"],
    "startDate": "2025-08-13",
    "endDate": "2025-08-29",
    "reason": "On leave"
  }' \
  | jq .

# Response:
# {
#   "delegationId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
#   "delegateFromUserId": "eve-user-id",
#   "delegateToUserId": "bob-user-id",
#   "grants": ["purchasing:approve-requisitions"],
#   "startDate": "2025-08-13",
#   "endDate": "2025-08-29",
#   "createdAt": "2025-08-13T10:15:00Z"
# }
```

#### Step 2: Create new requisition (Diana as requester)

```bash
curl -X POST $API_URL/api/purchase-requisitions \
  -H "Authorization: Bearer $DIANA_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "lines": [
      {
        "productId": "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "quantity": 50,
        "unitPrice": 75
      }
    ]
  }' \
  | jq '.requisitionId'

REQ_ID2="ffffffff-ffff-ffff-ffff-ffffffff0002"

# Submit it
curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID2/submit \
  -H "Authorization: Bearer $DIANA_TOKEN" \
  -d '{}'
```

#### Step 3: Bob (Deputy) approves using delegated authority ✓

```bash
BOB_TOKEN="..."

curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID2/approve \
  -H "Authorization: Bearer $BOB_TOKEN" \
  -d '{}' \
  | jq .

# Response (200):
# {
#   "requisitionId": "ffffffff-ffff-ffff-ffff-ffffffff0002",
#   "status": "Approved",
#   "approvedByUserId": "bob-user-id",
#   "approvedAt": "2025-08-13T10:18:00Z"
# }

# Framework validation chain:
# 1. Check SoD: diana != bob ✓
# 2. Check bob's grants: resolve via DelegationAwarePermissionResolver
#    - Direct grants: none
#    - Delegated grants: bob has "purchasing:approve-requisitions" from eve
#      (delegation is within date range 2025-08-13 to 2025-08-29)
#    - Result: HAS GRANT ✓
# 3. Proceed with approval
```

#### Step 4: Audit trail shows delegation

```bash
curl -X GET $API_URL/authorization/audit-log?resourceType=PurchaseRequisition&resourceId=$REQ_ID2 \
  -H "Authorization: Bearer $EVE_TOKEN" \
  | jq '.auditLog[] | select(.action == "Approved")'

# Response:
# {
#   "action": "Approved",
#   "userId": "bob-user-id",
#   "userName": "bob@acme",
#   "timestamp": "2025-08-13T10:18:00Z",
#   "delegatedFrom": "eve@acme",
#   "delegationId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
# }
# ^ Proves delegation was used
```

#### Step 5: Delegation expires after end date

```bash
# On 2025-08-30, Bob tries to approve another requisition:
curl -X POST $API_URL/api/purchase-requisitions/$REQ_ID3/approve \
  -H "Authorization: Bearer $BOB_TOKEN" \
  -d '{}' \
  | jq .

# Response (403):
# {
#   "error": "DELEGATION_EXPIRED",
#   "message": "Delegation has expired (ended 2025-08-29)",
#   "code": "Authorization.DelegationExpired"
# }
# Delegation is no longer valid; Bob must revert to direct grants only
```

**Framework pointer**: 
- Delegation storage: `Modulus.Authorization.Delegation.IDelegationStore`
- Permission resolution: `Modulus.Authorization.DelegationAwarePermissionResolver`
- Audit: `IEnterpriseAuditStore`

---

## Scenario 3: Field-Level Security

**What it demonstrates**: `[Classified]` fields, `FieldAuthorizer`, role-based field masking in same endpoint.

**What happens**: Same `GET /catalog/products/{id}` endpoint returns different JSON to Sales rep (cost/margin masked) vs Finance (cost/margin visible).

**Duration**: ~3 minutes

---

### 3.1 Sales Rep Views Product (Masked Fields)

```bash
ALICE_TOKEN="..."  # Sales rep

curl -X GET $API_URL/api/catalog/products/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  | jq .

# Response (Alice cannot see cost/margin):
# {
#   "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
#   "sku": "WIDGET-001",
#   "name": "Acme Widget",
#   "description": "High-quality widget",
#   "categoryId": "cat-001",
#   "unitPrice": 150.00,
#   "costPrice": null,      // ← MASKED (null/missing)
#   "margin": null,         // ← MASKED (null/missing)
#   "status": "Active",
#   "createdAt": "2025-08-01T00:00:00Z"
# }

# Framework filter applied:
# - Alice has role: "sales"
# - costPrice has [Classified] attribute with required role: "finance"
# - FieldAuthorizer checks: alice.roles includes "finance"?
#   Result: NO → field excluded from response
```

**Framework pointer**: `[Classified]` attribute on domain model

---

### 3.2 Finance Views Same Product (Unmasked Fields)

```bash
FRANK_TOKEN="..."  # Finance user

curl -X GET $API_URL/api/catalog/products/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa \
  -H "Authorization: Bearer $FRANK_TOKEN" \
  | jq .

# Response (Frank can see cost/margin):
# {
#   "productId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
#   "sku": "WIDGET-001",
#   "name": "Acme Widget",
#   "description": "High-quality widget",
#   "categoryId": "cat-001",
#   "unitPrice": 150.00,
#   "costPrice": 100.00,    // ← VISIBLE (Frank is Finance)
#   "margin": 0.3333,       // ← VISIBLE (calculated: (150-100)/150)
#   "status": "Active",
#   "createdAt": "2025-08-01T00:00:00Z"
# }

# Framework filter applied:
# - Frank has role: "finance"
# - costPrice has [Classified] attribute with required role: "finance"
# - FieldAuthorizer checks: frank.roles includes "finance"?
#   Result: YES → field included in response
```

**Key insight**: Same endpoint, same ID, different JSON based on user's role. No branching in handler code — all field masking is declarative via `[Classified]` + framework.

**Framework pointer**: 
- Attribute: `[Classified("finance")]` on `CostPrice` and `Margin` properties
- Authorizer: `Modulus.Authorization.FieldSecurity.FieldAuthorizer`
- Handler: Generic, no role checks → framework applies masking to DTO

---

### 3.3 Billing Invoice Shows Field Security in Action

```bash
# Alice (Sales rep) views invoice:
curl -X GET $API_URL/api/billing/invoices/11111111-1111-1111-1111-111111111111 \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  | jq '.invoice | {taxAmount, margin}'

# Response:
# {
#   "taxAmount": null,    // MASKED
#   "margin": null        // MASKED
# }

# Frank (Finance) views same invoice:
curl -X GET $API_URL/api/billing/invoices/11111111-1111-1111-1111-111111111111 \
  -H "Authorization: Bearer $FRANK_TOKEN" \
  | jq '.invoice | {taxAmount, margin}'

# Response:
# {
#   "taxAmount": 75.00,   // VISIBLE
#   "margin": 0.35        // VISIBLE (product cost pulled from Catalog)
# }
```

**Framework pointer**: `FieldSecurityProfile` in Billing module defines which fields are `[Classified]`

---

## Scenario 4: Org Scope & Data Scope

**What it demonstrates**: `IHasOrgUnit`, `ICurrentDataScope`, query-level filtering (predicate in SQL WHERE, not memory).

**What happens**: Same `GET /sales/orders` endpoint returns different rows based on user's org unit. Branch rep sees only their warehouse's orders, regional manager sees their region's orders, CFO sees all.

**Duration**: ~5 minutes

---

### Org Tree Setup

```
Company (root)
├── North Region (OrgUnitPath="/company/north/")
│   ├── NYC Warehouse (OrgUnitPath="/company/north/nyc/")
│   └── Boston Warehouse (OrgUnitPath="/company/north/boston/")
└── South Region (OrgUnitPath="/company/south/")
    ├── Miami Warehouse (OrgUnitPath="/company/south/miami/")
    └── Atlanta Warehouse (OrgUnitPath="/company/south/atlanta/")

Seed data:
- Alice: sales rep, assigned to NYC warehouse
- Bob: branch manager, assigned to NYC warehouse
- Charlie: regional manager, assigned to North Region
- Frank: CFO, company-level (sees all)
```

---

### 4.1 NYC Sales Rep Views Orders (Only NYC)

```bash
ALICE_TOKEN="..."  # Sales rep in NYC warehouse

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  | jq '.orders | length'

# Response: 3
# (Only Alice's orders + her warehouse's orders)

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  | jq '.orders[] | {orderId, orgUnitPath}'

# Response:
# [
#   {
#     "orderId": "order-001",
#     "orgUnitPath": "/company/north/nyc/"
#   },
#   {
#     "orderId": "order-002",
#     "orgUnitPath": "/company/north/nyc/"
#   },
#   {
#     "orderId": "order-003",
#     "orgUnitPath": "/company/north/nyc/"
#   }
# ]

# SQL Filter Applied:
# WHERE orders.OrgUnitPath LIKE '/company/north/nyc/%'
# (Filtering at database level, not in memory)
```

**Verify SQL predicate** (check server logs):

```
Executed DbCommand
[Parameters=]
SELECT ... FROM orders
WHERE orders.org_unit_path LIKE '/company/north/nyc/%'
```

---

### 4.2 Regional Manager Views Orders (All North Region)

```bash
CHARLIE_TOKEN="..."  # Regional manager for North Region

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $CHARLIE_TOKEN" \
  | jq '.orders | length'

# Response: 6
# (All North Region orders: NYC + Boston)

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $CHARLIE_TOKEN" \
  | jq '.orders[] | .orgUnitPath' | sort | uniq

# Response:
# "/company/north/nyc/"
# "/company/north/boston/"

# SQL Filter Applied:
# WHERE orders.OrgUnitPath LIKE '/company/north/%'
# (Broader filter for regional manager)
```

---

### 4.3 CFO Views Orders (All Company)

```bash
FRANK_TOKEN="..."  # CFO, company-level access

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $FRANK_TOKEN" \
  | jq '.orders | length'

# Response: 12
# (All company orders across all regions)

curl -X GET $API_URL/api/sales/orders?skip=0&take=20 \
  -H "Authorization: Bearer $FRANK_TOKEN" \
  | jq '.orders[] | .orgUnitPath' | sort | uniq

# Response:
# "/company/north/boston/"
# "/company/north/nyc/"
# "/company/south/atlanta/"
# "/company/south/miami/"

# SQL Filter Applied:
# (no WHERE clause for org scope — CFO has global view)
```

---

### 4.4 Verify Query-Level Filtering (Not Memory Filtering)

The framework applies `ICurrentDataScope` predicate at the EF Core query level. Proof:

```bash
# Check server logs for SQL queries
docker logs modulus-sample-api 2>&1 | grep -A5 "WHERE.*org_unit_path"

# Output:
# DbCommand: SELECT ... FROM sales_schema.orders
# WHERE orders.org_unit_path LIKE '/company/north/nyc/%'
# Parameters: []
# (Filter is in WHERE clause, not post-fetch filtering)
```

**Framework pointer**:
- Interface: `Modulus.Security.Scope.ICurrentDataScope`
- Implementation: `ModuleDbContext.OnModelCreating()` → `.HasQueryFilter()`
- Usage in Sales module:

```csharp
modelBuilder.Entity<SalesOrder>().HasQueryFilter(
    o => EF.Functions.Like(o.OrgUnitPath, 
        _currentDataScope.OrgUnitPath + "%"));
```

---

## Scenario 5: Feature Entitlements

**What it demonstrates**: `IFeatureEntitlementResolver`, plan-based access, feature gates.

**What happens**:
1. StartUp Inc (Starter plan) tries multi-warehouse transfer → denied
2. Acme Corp (Enterprise plan) performs same transfer → allowed
3. AR-aging report: Starter plan → denied, Enterprise plan → allowed

**Duration**: ~5 minutes

---

### 5.1 Multi-Warehouse Transfer: Plan-Based Access

#### Setup: Two tenants

```
Acme Corp (Enterprise plan)
  ├── Warehouse A: 100 units of Product X
  └── Warehouse B: 0 units of Product X

StartUp Inc (Starter plan)
  ├── Warehouse S1: 50 units of Product Y
  └── Warehouse S2: 0 units of Product Y
```

#### Step 1: StartUp (Starter) attempts transfer ❌

```bash
STARTUP_ALICE_TOKEN="..."  # Sales rep at StartUp Inc (Starter plan)

curl -X POST $API_URL/api/inventory/stock-movements/transfer \
  -H "Authorization: Bearer $STARTUP_ALICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fromWarehouseId": "warehouse-s1-id",
    "toWarehouseId": "warehouse-s2-id",
    "productId": "product-y-id",
    "quantity": 25
  }' \
  | jq .

# Response (403):
# {
#   "error": "FEATURE_NOT_AVAILABLE",
#   "message": "Multi-warehouse transfers require Enterprise plan",
#   "code": "FeatureGate.NotAvailable",
#   "requiredPlan": "Enterprise",
#   "currentPlan": "Starter"
# }

# Framework check:
# 1. Command reaches handler
# 2. Handler decorated: @FeatureGate("inventory:multi-warehouse-transfer")
# 3. Behavior intercepts: resolves via IFeatureEntitlementResolver
# 4. Resolver checks: startup-inc tenant plan (Starter) includes feature?
#    Result: NO → deny
# 5. Returns 403 with feature requirement
```

#### Step 2: Acme (Enterprise) performs transfer ✓

```bash
ALICE_TOKEN="..."  # Sales rep at Acme Corp (Enterprise plan)

curl -X POST $API_URL/api/inventory/stock-movements/transfer \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fromWarehouseId": "warehouse-a-id",
    "toWarehouseId": "warehouse-b-id",
    "productId": "product-x-id",
    "quantity": 50
  }' \
  | jq .

# Response (200):
# {
#   "movementId": "transfer-001",
#   "status": "Completed",
#   "fromWarehouse": "Warehouse A",
#   "toWarehouse": "Warehouse B",
#   "quantity": 50,
#   "createdAt": "2025-08-13T10:45:00Z"
# }

# Framework check:
# 1. Command reaches handler
# 2. Handler decorated: @FeatureGate("inventory:multi-warehouse-transfer")
# 3. Behavior intercepts: resolves via IFeatureEntitlementResolver
# 4. Resolver checks: acme-corp tenant plan (Enterprise) includes feature?
#    Result: YES → allow
# 5. Proceeds with normal handling
```

**Framework pointer**: `Modulus.Features.FeatureGate` attribute, `IFeatureEntitlementResolver`

---

### 5.2 AR-Aging Report: Finance Feature Gate

#### Step 1: StartUp Finance requests AR-aging (Starter plan) ❌

```bash
STARTUP_FRANK_TOKEN="..."  # Finance user at StartUp Inc (Starter plan)

curl -X GET $API_URL/api/billing/reports/ar-aging \
  -H "Authorization: Bearer $STARTUP_FRANK_TOKEN" \
  | jq .

# Response (403):
# {
#   "error": "FEATURE_NOT_AVAILABLE",
#   "message": "AR aging reports require Enterprise plan",
#   "code": "FeatureGate.NotAvailable",
#   "requiredPlan": "Enterprise"
# }

# Handler is decorated: @FeatureGate("billing:ar-reports")
# Startup plan does NOT include this feature
```

#### Step 2: Acme Finance requests AR-aging (Enterprise plan) ✓

```bash
FRANK_TOKEN="..."  # Finance user at Acme Corp (Enterprise plan)

curl -X GET $API_URL/api/billing/reports/ar-aging \
  -H "Authorization: Bearer $FRANK_TOKEN" \
  | jq .

# Response (200):
# {
#   "reportDate": "2025-08-13",
#   "aging": {
#     "current": 50000,         # 0-30 days
#     "days30": 25000,          # 31-60 days
#     "days60": 10000,          # 61-90 days
#     "days90Plus": 5000        # 90+ days
#   },
#   "totalOutstanding": 90000,
#   "overdue": 15000,
#   "generatedAt": "2025-08-13T10:50:00Z"
# }

# Handler is decorated: @FeatureGate("billing:ar-reports")
# Acme plan INCLUDES this feature → proceeds
```

---

### 5.3 Upgrade to Enterprise

#### Step 1: Check tenant plan

```bash
STARTUP_ADMIN_TOKEN="..."  # Admin at StartUp Inc

curl -X GET $API_URL/authorization/tenants/current \
  -H "Authorization: Bearer $STARTUP_ADMIN_TOKEN" \
  | jq '.plan'

# Response: "Starter"
```

#### Step 2: Upgrade plan (administrative call)

```bash
# (Admin portal or backend API — simplified here)
curl -X PUT $API_URL/authorization/tenants/current/plan \
  -H "Authorization: Bearer $STARTUP_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "newPlan": "Enterprise"
  }' \
  | jq .

# Response:
# {
#   "tenantId": "startup-inc-id",
#   "plan": "Enterprise",
#   "effectiveFrom": "2025-08-13T10:55:00Z"
# }
```

#### Step 3: Retry blocked features (now allowed)

```bash
# StartUp Finance now attempts AR-aging (Enterprise plan):
curl -X GET $API_URL/api/billing/reports/ar-aging \
  -H "Authorization: Bearer $STARTUP_FRANK_TOKEN" \
  | jq '.aging.total'

# Response: 45000
# (Previously denied, now allowed)

# StartUp Inventory transfers (now allowed):
curl -X POST $API_URL/api/inventory/stock-movements/transfer \
  -H "Authorization: Bearer $STARTUP_ALICE_TOKEN" \
  -d '{...}' \
  | jq '.status'

# Response: "Completed"
```

**Framework pointer**: 
- Feature gate attribute: `Modulus.Features.FeatureGate("feature-name")`
- Entitlement resolver: `Modulus.Features.IFeatureEntitlementResolver`
- Plan storage: `Modulus.Tenants.ITenantStore` (plan field)

---

## Verification Checklist

After completing all 5 scenarios, verify:

- [ ] Scenario 1 (Saga): Order created → submitted → saga starts → stock reserved → invoice created → order final status=Invoiced
- [ ] Scenario 1 (Compensation): Order with insufficient stock → saga fails → compensation executes → order cancelled → stock NOT reserved
- [ ] Scenario 2 (SoD): Requester cannot approve own requisition (403 error)
- [ ] Scenario 2 (Approval): Different user can approve (200 OK)
- [ ] Scenario 2 (Delegation): Deputy approves using delegated authority (200 OK, audit shows delegation)
- [ ] Scenario 3 (Sales rep): Product viewed without cost/margin (null values)
- [ ] Scenario 3 (Finance): Product viewed with cost/margin (visible values)
- [ ] Scenario 4 (Branch rep): Sees only their warehouse orders (3 orders)
- [ ] Scenario 4 (Regional manager): Sees region's orders (6 orders)
- [ ] Scenario 4 (CFO): Sees all orders (12 orders)
- [ ] Scenario 5 (Starter plan): Multi-warehouse transfer denied (403), AR-aging denied (403)
- [ ] Scenario 5 (Enterprise plan): Multi-warehouse transfer allowed (200), AR-aging allowed (200)

---

## Troubleshooting

### Saga not completing

```bash
# Check Quartz job logs
docker logs modulus-sample-api 2>&1 | grep -i "outbox"

# Check RabbitMQ
docker exec rabbitmq rabbitmq-diagnostics queues

# Verify integration event handler is registered
# See: src/Modules/{Module}/Infrastructure/{Module}Module.cs
# Should have: services.AddIntegrationEventHandler<Event, Handler>();
```

### Field security not applying

```bash
# Verify [Classified] attribute on domain model
# See: src/Modules/Catalog/Domain/Product.cs
# Property should have: [Classified("finance")]

# Verify FieldAuthorizer is wired in Program.cs
# Should have: app.AddFieldSecurity();
```

### Org scope not filtering

```bash
# Check ICurrentDataScope is injected into DbContext
# See: src/Modules/{Module}/Infrastructure/{Module}DbContext.cs
# Should have: .HasQueryFilter(o => filter based on _currentDataScope)

# Verify query execution includes WHERE predicate
# Check SQL logs: docker logs modulus-sample-api 2>&1 | grep "WHERE.*org_unit_path"
```

### Feature gate not denying access

```bash
# Verify @FeatureGate attribute on handler/endpoint
# See: src/Modules/{Module}/Application/Commands/{Command}Handler.cs

# Verify IFeatureEntitlementResolver is registered
# See: Program.cs should have: services.AddFeatureEntitlements();

# Check tenant plan in database
# psql: SELECT * FROM tenants.tenants;
```

---

## Next Steps

After verifying all scenarios:

1. **Customize for your domain**: Replace product/warehouse/customer IDs with real data
2. **Load testing**: Use Locust or K6 to simulate concurrent orders during peak hours
3. **Add reporting**: Extend AR-aging to include other metrics (days sales outstanding, collection rate)
4. **Extend workflows**: Add additional saga steps (e.g., quality inspection before shipment)
5. **Mobile app**: Build mobile frontend consuming these APIs (React Native or Flutter)

---

## Framework References

- **Modulus Sagas**: `Modulus.Sagas` namespace, ISaga<T>, CompensateAsync()
- **Outbox/Inbox**: `Modulus.Outbox` namespace, IEventBus.PublishAsync()
- **Field Security**: `Modulus.Authorization.FieldSecurity`, [Classified] attribute
- **Org Scope**: `Modulus.Security.Scope`, ICurrentDataScope, HasQueryFilter()
- **Feature Entitlements**: `Modulus.Features`, FeatureGate, IFeatureEntitlementResolver
- **SoD & Delegation**: `Modulus.Authorization`, SodPolicy, IDelegationStore

