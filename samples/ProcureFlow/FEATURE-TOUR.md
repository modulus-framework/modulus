# Modulus Framework Feature Tour

This document showcases the 5 key scenarios demonstrating Modulus framework capabilities through the ModulusSampleErp business domain.

## Prerequisites

1. Start the application: `dotnet run --project src/API/ModulusSample.Api -- --seed`
2. Wait for migrations and seeding to complete
3. API runs on `https://localhost:5001` (adjust port if needed)

## Demo Setup

The sample seeds a realistic B2B wholesale distribution ERP with:

**Org Hierarchy**: Company → 2 Regions → 4 Branches  
**Personas**:
- Sales Rep (sells to customers)
- Branch Manager (manages branch operations)
- Regional Manager (oversees region)
- Buyer (creates purchase requisitions)
- Purchasing Manager (approves requisitions)
- Finance (manages invoicing and payments)

---

## Scenario 1: Order-to-Cash Saga

**Framework Feature**: `Modulus.Sagas`, `Modulus.Outbox`, transactional inbox/outbox

**What It Demonstrates**: 
Multi-step business process with compensation path. When a sales order is created, an inventory saga reserves stock, picks goods, ships, and creates an invoice. If stock is insufficient, the saga compensates by releasing the reservation.

### Test: Create and Process an Order

```bash
# 1. Create a sales order
curl -X POST https://localhost:5001/api/sales-orders \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -d '{
    "orderNumber": "SO-2026-001",
    "customerId": "10000000-0000-0000-0000-000000000001",
    "orderDate": "2026-01-15"
  }' | jq .

# 2. View the order (saga has reserved inventory and progressed the workflow)
ORDER_ID="<order-id-from-step-1>"
curl https://localhost:5001/api/sales-orders/$ORDER_ID \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" | jq .

# Expected: Order moves through Draft → Confirmed → Shipped → Invoiced
# Saga automatically:
# ✓ Reserved stock from warehouse
# ✓ Picked goods from location
# ✓ Shipped to customer
# ✓ Created invoice in Billing module
```

---

## Scenario 2: Segregation of Duties + Delegation

**Framework Feature**: `SodPolicy`, `IDelegationStore`, `DelegationAwarePermissionResolver`

**What It Demonstrates**:
A business rule enforced at the domain level: the requester cannot approve their own purchase requisition. The framework validates this at authorization time.

### Test: Attempt SoD Violation (will fail correctly)

```bash
# 1. Create a requisition as Buyer (ID: 10000000-0000-0000-0000-000000000004)
curl -X POST https://localhost:5001/api/purchase-requisitions \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000004" \
  -d '{
    "requisitionNumber": "REQ-2026-SoD-Test",
    "orgUnitId": "00000000-0000-0000-0000-000000000111"
  }' | jq .

REQ_ID="<requisition-id-from-above>"

# 2. Submit the requisition (allowed - buyer is submitting their own)
curl -X POST https://localhost:5001/api/purchase-requisitions/$REQ_ID/submit \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000004" | jq .

# 3. Attempt to approve as same user (will fail with SoD violation)
curl -X POST https://localhost:5001/api/purchase-requisitions/$REQ_ID/approve \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000004" \
  -d '{
    "approverId": "10000000-0000-0000-0000-000000000004"
  }' | jq .

# Expected Error Response:
# {
#   "error": "The requester cannot approve their own requisition (Segregation of Duties)"
# }

# 4. Approve as Purchasing Manager (works correctly)
curl -X POST https://localhost:5001/api/purchase-requisitions/$REQ_ID/approve \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000005" \
  -d '{
    "approverId": "10000000-0000-0000-0000-000000000005"
  }' | jq .

# Expected: Success (200 OK)
```

---

## Scenario 3: Field-Level Security

**Framework Feature**: `[Classified]`, `FieldSecurityProfile`, `FieldAuthorizer`

**What It Demonstrates**:
The same `GET /api/catalog/products/{id}` endpoint returns different fields based on the caller's role. A Finance user sees `UnitCost` and `Margin`. A Sales Rep sees only public fields. The security is enforced via field-level attributes on the domain entity.

### Test: Role-Based Field Visibility

```bash
# Product ID (seeded)
PRODUCT_ID="<product-id-from-catalog>"

# 1. Query as Sales Rep (sees only public fields)
curl https://localhost:5001/api/catalog/products/$PRODUCT_ID \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000001" \
  -H "X-Roles: SalesRep" | jq .

# Expected: Returns { id, name, listPrice, ... } (no UnitCost, no Margin)

# 2. Query as Finance user (sees cost/margin fields)
curl https://localhost:5001/api/catalog/products/$PRODUCT_ID \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000006" \
  -H "X-Roles: Finance" | jq .

# Expected: Returns { id, name, listPrice, unitCost, margin, ... }
# (same endpoint, different response shape based on [Classified] attributes)
```

---

## Scenario 4: Organizational Scope + Data Scope

**Framework Feature**: `IHasOrgUnit`, `IHasOwner`, `OrgScopeResolver`, `ModuleDbContext` query filters

**What It Demonstrates**:
The same `GET /api/sales-orders` returns different rows for a Branch Rep, Regional Manager, and CFO — enforced as a SQL predicate via query filters, not filtered in memory.

### Test: Scope-Based Row Filtering

```bash
# 1. Branch Rep sees only their branch's orders
curl https://localhost:5001/api/sales-orders \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000002" \
  -H "X-OrgUnit-Id: 00000000-0000-0000-0000-000000000111" | jq '.data | length'

# Expected: 5 orders (branch 1 scope)

# 2. Regional Manager sees 2 branches' orders
curl https://localhost:5001/api/sales-orders \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000003" \
  -H "X-OrgUnit-Id: 00000000-0000-0000-0000-000000000011" | jq '.data | length'

# Expected: 10 orders (region 1 = branches 1+2)

# 3. CFO sees all orders
curl https://localhost:5001/api/sales-orders \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-User-Id: 10000000-0000-0000-0000-000000000006" | jq '.data | length'

# Expected: 20 orders (all branches)
# (same endpoint, rows filtered at database layer via IHasOrgUnit scope)
```

---

## Scenario 5: Feature Entitlements

**Framework Feature**: `IFeatureEntitlementResolver`, `FeatureGate`, mediator feature-gate behavior

**What It Demonstrates**:
A multi-warehouse transfer returns `Denied` on the Starter plan and succeeds on Enterprise, with no code difference—the feature gate behavior is registered at composition time.

### Test: Plan-Gated Feature (Multi-Warehouse)

```bash
# 1. Create a stock transfer on Starter plan (denied)
curl -X POST https://localhost:5001/api/warehouses/transfer \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-Plan: Starter" \
  -d '{
    "fromWarehouseId": "00000000-0000-0000-0000-000000000111",
    "toWarehouseId": "00000000-0000-0000-0000-000000000121",
    "quantity": 100,
    "productId": "<product-id>"
  }' | jq .

# Expected Error:
# {
#   "error": "Feature not available on your plan. Upgrade to Enterprise."
# }

# 2. Same request on Enterprise plan (succeeds)
curl -X POST https://localhost:5001/api/warehouses/transfer \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000000" \
  -H "X-Plan: Enterprise" \
  -d '{
    "fromWarehouseId": "00000000-0000-0000-0000-000000000111",
    "toWarehouseId": "00000000-0000-0000-0000-000000000121",
    "quantity": 100,
    "productId": "<product-id>"
  }' | jq .

# Expected: Success (200 OK, transfer created)
# (same endpoint, different behavior based on tenant's plan entitlement)
```

---

## Testing Checklist

After running each scenario:

- [ ] **Scenario 1**: Saga successfully progressed order through all states
- [ ] **Scenario 2**: SoD violation caught at domain layer (not bypassed)
- [ ] **Scenario 3**: Field visibility enforced without client-side filtering
- [ ] **Scenario 4**: Row scope enforced at database layer (check SQL logs)
- [ ] **Scenario 5**: Feature gate blocks or allows based on plan

---

## Architecture Files to Explore

After the scenarios, explore these framework integration points:

| Scenario | File Path |
|----------|-----------|
| 1. Sagas | `src/Modules/Sales/Domain/Entities/OrderFulfillmentSaga.cs` |
| 2. SoD | `src/Modules/Purchasing/Domain/Entities/PurchaseRequisition.cs:95` (Approve method) |
| 3. Field Security | `src/Modules/Catalog/Domain/Entities/Product.cs` ([Classified] attributes) |
| 4. Org Scope | `src/Modules/Sales/Infrastructure/Database/SalesDbContext.cs` (query filters) |
| 5. Entitlements | `src/API/ModulusSample.Api/Program.cs` (feature gate registration) |

---

## Curl Helpers

```bash
# Set tenant header for all requests
export TENANT_ID="00000000-0000-0000-0000-000000000000"
export API_URL="https://localhost:5001"

# Health check
curl $API_URL/health | jq .

# List all modules via Swagger
curl $API_URL/swagger/v1/swagger.json | jq '.paths | keys'
```

---

**Generated by Claude Code** — Full implementation details in the commit history and PHASE1_STATUS.md
