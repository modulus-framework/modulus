# ModulusSampleErp Documentation

Complete documentation for transforming ModulusSampleErp into a production-ready B2B ERP sample that demonstrates all Modulus framework capabilities.

---

## Quick Links

| Document | Purpose | Audience |
|----------|---------|----------|
| **[IMPLEMENTATION_PLAN.md](./IMPLEMENTATION_PLAN.md)** | Step-by-step plan for building 6 business modules in 3 phases | Project managers, developers |
| **[ARCHITECTURE.md](./ARCHITECTURE.md)** | Domain models, layers, patterns, cross-module communication | Architects, senior developers |
| **[FEATURE-TOUR.md](./FEATURE-TOUR.md)** | Live curl scripts for 5 scenarios demonstrating framework features | QA, sales, end users |
| **[README.md](./README.md)** | This file — overview and navigation | Everyone |

---

## What is ModulusSampleErp?

A **modular monolith** ERP sample for the Modulus framework. It models a B2B wholesale distributor with:

- **6 business modules**: Catalog, Partners, Inventory, Sales, Purchasing, Billing
- **7 platform modules**: Identity, Tenants, Settings, Features, VirtualFileExplorer, Notifications, Media
- **5 demo scenarios**: showcasing sagas, SoD, field security, org scope, feature entitlements

**Why it matters**:
- Demonstrates real enterprise complexity (not just CRUD)
- Proves Modulus handles approvals, workflows, security, multi-tenancy
- Differentiates against competitors (eShop, Contoso)

---

## Getting Started

### Prerequisites

```bash
# Install .NET 10
dotnet --version

# Clone repo & open solution
code ModulusSampleErp.slnx

# Start infrastructure
cd path/to/repo
docker compose up -d

# Verify services ready
docker ps  # All 5 services healthy (postgres, redis, rabbitmq, minio, api)
```

### First Run: Seed Database

```bash
dotnet run --project src/API/ModulusSample.Api -- --seed

# Expected output:
# Seeding database...
# Creating tenants: Acme Corp, StartUp Inc
# Creating org tree...
# Creating 6 personas...
# Creating sample products, customers, warehouses...
# Done. Database ready.
```

### Walk First Scenario (2 min)

```bash
# Open FEATURE-TOUR.md, Scenario 1, copy-paste curl commands
# See order created → saga triggered → stock reserved → invoice created

curl http://localhost:8080/api/sales/orders \
  -H "Authorization: Bearer <token>" \
  -d '...'
```

---

## Documentation Structure

### 1. IMPLEMENTATION_PLAN.md

**Contains**:
- Executive summary (what, why, timeline)
- Architecture patterns & value objects
- 6 module specifications (domain models, CQRS, endpoints)
- 5 prerequisite fixes (sagas, auth endpoints, seed data, media normalization)
- 3 implementation phases (week-by-week breakdown)
- 5 demo scenarios (what each tests)
- Testing strategy (unit, integration, E2E)
- File structure & checklist

**Use this to**:
- Understand the scope ("what are we building?")
- Plan your sprints ("which phase first?")
- Design your modules ("what's the domain model?")
- Write tests ("what should pass?")

**Key sections**:
- Section 2: Architecture & patterns (Value Objects, Specification)
- Section 3: Module specs (Catalog, Partners, Inventory, Sales, Purchasing, Billing)
- Section 4: Prerequisite fixes (do these first)
- Section 5: Phases (Phase 1 = spine modules, Phase 2 = governance, Phase 3 = money)

---

### 2. ARCHITECTURE.md

**Contains**:
- Layered architecture diagram (Presentation → Application → Domain → Infrastructure)
- Cross-module communication patterns (sync vs async)
- Domain models for all 6 modules (with UML-style tables)
- Value object examples (Money, Quantity, Email)
- Specification pattern for complex queries
- Data flow examples (saga, SoD+delegation)
- Technology stack

**Use this to**:
- Understand module interactions ("how do Sales & Inventory communicate?")
- Design new aggregates ("what should Product look like?")
- Model domain events ("what should ProductCreatedDomainEvent contain?")
- Implement queries ("when do I use Specification?")

**Key sections**:
- Section 2: Layers (each module's structure)
- Section 3: Communication (sync/async patterns)
- Section 4: Module domain models (the actual shapes)
- Section 6: Value Objects (patterns to reuse)
- Section 8: Data flow examples (saga, SoD, payment processing)

---

### 3. FEATURE-TOUR.md

**Contains**:
- 5 complete scenario walkthroughs with curl commands
- Happy paths and unhappy paths (compensation, errors)
- Step-by-step verification for each scenario
- Framework pointer to relevant source files
- Troubleshooting section

**Scenarios**:
1. **Order-to-Cash Saga** (order created → saga → invoice) — tests Modulus.Sagas
2. **SoD + Delegation** (buyer can't approve own requisition, delegate authority) — tests SodPolicy, IDelegationStore
3. **Field-Level Security** (same endpoint, different JSON by role) — tests [Classified], FieldAuthorizer
4. **Org Scope** (warehouse rep sees only their branch's orders) — tests IHasOrgUnit, query filters
5. **Feature Entitlements** (multi-warehouse transfer denied on Starter plan) — tests FeatureGate

**Use this to**:
- Test your implementation ("do scenarios 1-5 still pass?")
- Demo to stakeholders ("here's order fulfillment working end-to-end")
- Verify framework features ("does field security actually mask fields?")
- Debug integration issues ("where's the saga stuck?")

---

## Recommended Reading Order

### For Project Leads
1. IMPLEMENTATION_PLAN.md § 1-2 (Executive summary, patterns)
2. IMPLEMENTATION_PLAN.md § 5 (Phases, timeline)
3. FEATURE-TOUR.md § Overview (What are the scenarios?)

### For Developers (Building Modules)
1. IMPLEMENTATION_PLAN.md § 2 (Patterns & value objects)
2. ARCHITECTURE.md § 4 (Domain models for your module)
3. IMPLEMENTATION_PLAN.md § 3 (Your module's spec)
4. IMPLEMENTATION_PLAN.md § 7 (Testing approach)

### For QA / Testers
1. FEATURE-TOUR.md (All 5 scenarios)
2. IMPLEMENTATION_PLAN.md § 8 (Success criteria)
3. FEATURE-TOUR.md § Verification checklist

### For Sales / Product
1. FEATURE-TOUR.md § Overview (5 scenarios)
2. FEATURE-TOUR.md § 1.1, 2.1, 3.1, 4.1, 5.1 (Happy paths only)
3. ARCHITECTURE.md § Overview (What the architecture enables)

---

## Key Concepts Reference

### Modulus Framework Concepts

| Concept | Purpose | Location |
|---------|---------|----------|
| **Endpoint<TRequest, TResponse>** | REPR handler class (replaces MapGroup) | Presentation layer |
| **ICommand, IQuery** | CQRS (via Modulus.Mediator) | Application layer |
| **AggregateRoot<TId>** | Domain model base class | Domain layer |
| **IDomainEvent** | Event raised by aggregate | Domain layer |
| **ISaga<TSagaData>** | Multi-step workflow with compensation | Application layer (Modulus.Sagas) |
| **IIntegrationEvent** | Cross-module event record | IntegrationEvents project |
| **IEventBus** | Publish integration event → Outbox | Application layer |
| **Modulus.Outbox** | Guarantees event delivery (transactional) | Infrastructure (automatic) |
| **ICurrentDataScope** | Filter rows by user's org unit | Infrastructure (query filters) |
| **[Classified]** | Mask field from unauthorized roles | Domain model attribute |
| **FeatureGate** | Gate feature by plan | Handler attribute |
| **SodPolicy** | Segregation of duties validation | Authorization |
| **IDelegationStore** | Time-bounded authority transfer | Authorization |

### Project Conventions

| Pattern | Applies To | Example |
|---------|-----------|---------|
| **ValueObject** | Immutable domain concepts | `Money`, `Quantity`, `Email` |
| **Result<T>** | CQRS handler return type | `Result<Guid>` (order ID) |
| **Specification<T>** | Complex reusable queries | `PurchaseRequisitionsAwaitingApprovalSpec` |
| **Feature folder structure** | Code organization | `Products/`, `Orders/`, `Requisitions/` |
| **Schema-per-module** | Database isolation | `catalog_schema`, `sales_schema` |
| **Per-module migrations** | EF Core tracking | `__EFMigrationsHistory` per schema |
| **Domain → Integration event** | Async pub-sub | `ProductCreatedDomainEvent` → `ProductCreatedIntegrationEvent` |

---

## Development Workflow

### Phase 1 (Weeks 1-3): Spine Modules

**Modules**: Catalog, Partners, Inventory, Sales

1. **Week 1: Catalog**
   - Domain: Product, ProductCategory, UnitOfMeasure
   - Value objects: Money, Quantity
   - Redis caching
   - 5 REPR endpoints
   - Integration tests

2. **Week 1-2: Partners** (parallel)
   - Domain: Customer, Supplier, Contact
   - PII encryption, data scope
   - 6 REPR endpoints
   - Integration tests

3. **Week 2: Inventory**
   - Domain: Warehouse, StockItem, StockReservation
   - Org scope, optimistic concurrency
   - Event handlers (subscribe to Sales)
   - 5 REPR endpoints

4. **Week 3: Sales**
   - Domain: SalesOrder, SalesOrderLine, Shipment
   - **Saga** (OrderFulfillmentSaga) with compensation
   - Workflow policy (state machine)
   - 6 REPR endpoints
   - E2E saga tests

**Delivers**: Scenarios 1, 3, 4 (Saga, Field security, Org scope)

### Phase 2 (Weeks 4-5): Governance

**Module**: Purchasing

- Domain: PurchaseRequisition, PurchaseOrder, GoodsReceipt
- SoD enforcement (requester ≠ approver)
- Delegation (time-bounded authority)
- Approval queue (Specification pattern)
- 8 REPR endpoints
- SoD + delegation tests

**Delivers**: Scenario 2 (SoD + Delegation)

### Phase 3 (Weeks 6-7): Money

**Module**: Billing

- Domain: Invoice, Payment, CreditNote
- Cross-module events (consumes Sales/Purchasing events)
- Field security ([Classified] on TaxAmount, Margin)
- Feature entitlements (AR-aging on Enterprise)
- 3 Quartz background jobs
- 8 REPR endpoints
- Feature gate tests

**Delivers**: Scenario 5 (Feature entitlements)

### Week 8: Documentation & Demo

- Write FEATURE-TOUR.md scenarios
- Record demo video
- Create Postman collection
- Verify all 5 scenarios pass
- Build release candidate

---

## Testing Strategy

### Unit Tests
- **What**: Aggregates, value objects, domain logic
- **Example**: `SalesOrderTests.Create_WithValidInputs_ReturnsSuccessResult()`
- **Framework**: xUnit + Fluent Assertions

### Integration Tests
- **What**: CQRS handlers, event handlers, saga steps
- **Example**: `CreateSalesOrderHandlerTests.Handle_WithValidCommand_CreatesOrder()`
- **Framework**: ModulusWebAppFactory (in-memory SQLite)

### E2E Scenario Tests
- **What**: Full workflow (order → saga → invoice)
- **Example**: `OrderToSagaScenarioTests.OrderSubmission_ReservesStock_AndCreatesInvoice()`
- **Framework**: ModulusWebAppFactory + HTTP client

### Scenario Verification (Manual)
- **What**: 5 scenarios via curl commands
- **Where**: FEATURE-TOUR.md
- **How**: Copy-paste curl, verify response

---

## Deployment Checklist

Before production deployment:

```bash
# Build
dotnet build ModulusSampleErp.slnx
# Expected: 0 warnings, 0 errors (TreatWarningsAsErrors is global)

# Test
dotnet test tests/
# Expected: All tests pass (unit, integration, E2E)

# Docker
docker build -t modulus-sample-erp:latest .
docker compose -f docker-compose.prod.yml up -d
# Expected: All services healthy

# Seed
docker exec api dotnet run -- --seed
# Expected: Migrations run, data seeded

# Verify scenarios
bash scripts/verify-scenarios.sh
# Expected: All 5 scenarios pass

# Health check
curl http://localhost:8080/health
# Expected: 200 OK
```

---

## Support & References

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Saga not completing | Check Quartz logs in docker logs, verify IntegrationEventHandler is registered |
| Field security not masking | Verify [Classified] attribute on domain model, check FieldAuthorizer in Program.cs |
| Org scope not filtering | Verify query filter in DbContext.OnModelCreating(), check SQL WHERE predicate in logs |
| Feature gate not working | Verify @FeatureGate attribute on handler, check tenant plan in database |

### Useful Commands

```bash
# View logs
docker logs -f modulus-sample-api
docker logs -f modulus-sample-postgres

# Access database
psql -h localhost -U ModulusSample -d ModulusSample

# Check message queue
docker exec rabbitmq rabbitmq-diagnostics queues

# Clear database (for fresh start)
docker volume rm modulus-sample-erp_postgres-data
docker compose up -d postgres
dotnet run --project src/API/ModulusSample.Api -- --seed
```

### Framework Documentation

- **Modulus GitHub**: https://github.com/modulus-framework/
- **Modulus Docs**: `PROJECT_STRUCTURE.md` in framework repo
- **.NET 10 Docs**: https://learn.microsoft.com/dotnet
- **Entity Framework Core**: https://learn.microsoft.com/ef/core
- **Rebus (Message Bus)**: https://github.com/rebus-org/Rebus

---

## Contributing

When modifying this documentation:

1. **IMPLEMENTATION_PLAN.md**: Update phases, prerequisites, or checklist
2. **ARCHITECTURE.md**: Add domain models, data flows, patterns
3. **FEATURE-TOUR.md**: Add/update scenario curl commands
4. **README.md**: Update navigation, key concepts

Ensure all code examples compile and all curl commands are tested against running API.

---

## FAQ

**Q: Should I start with Phase 1, 2, 3 in order?**  
A: Yes. Phase 1 (Catalog, Partners, Inventory, Sales) is the spine. Phase 2 (Purchasing) and Phase 3 (Billing) extend it. Prerequisite fixes must happen first.

**Q: Do I need to implement all scenarios?**  
A: Phase 1 alone delivers scenarios 1, 3, 4 (saga, field security, org scope). Scenario 2 (SoD + delegation) requires Phase 2. Scenario 5 (feature entitlements) requires Phase 3. Implement what your stakeholders need.

**Q: Can I skip Value Objects?**  
A: Not recommended. They prevent invalid states at compile time. Use Money for all monetary amounts, Quantity for all quantities.

**Q: What if I need to add a 7th business module?**  
A: Follow the same pattern: Domain (aggregates, events) → Application (CQRS) → Infrastructure (DbContext) → Presentation (REPR endpoints). Reference the Catalog/Sales module templates.

**Q: How do I integrate with external systems?**  
A: Use IntegrationEventHandlers to subscribe to module events. For outbound calls (e.g., payment gateway), call external service in handler or command. Never call across modules except via PublicApi or events.

**Q: Do I need to use all 3 tiers of org hierarchy (Company → Region → Warehouse)?**  
A: Not strictly. You can use 2 tiers (Company → Warehouse) or 4+ tiers (Company → Region → District → Branch). OrgUnit path filtering is flexible.

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-08-13 | Initial documentation (3 phases, 6 modules, 5 scenarios) |

---

## Contact

For questions about ModulusSampleErp:
- **Architecture**: Check ARCHITECTURE.md or code comments
- **Implementation**: Refer to IMPLEMENTATION_PLAN.md § 3-4
- **Scenarios**: Use curl commands in FEATURE-TOUR.md to test

