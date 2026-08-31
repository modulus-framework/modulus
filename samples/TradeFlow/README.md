# TradeFlow

A comprehensive **modular ERP system** for cross-border trade, built on the **Modulus** modular-monolith framework for .NET 10.

This sample demonstrates a real-world enterprise application implementing a complete import procurement workflow:

- **12 complete modules** — platform foundation + 8 business modules covering the full import-to-stock lifecycle
- **4-layer Clean Architecture** per module (Domain → Application → Infrastructure → Presentation)
- **Per-module DbContext** with EF Core migrations (or SQLite for development)
- **Production-ready features** — rate limiting, security headers, idempotency, observability, correlation IDs
- **Full BRS suite** — authoritative business requirements in `docs/BRS/` (~150 rules across 8 business modules)

## Architecture

### Module System

The application uses the **Modulus module system** with clean 4-layer separation of concerns per module:

Each business module follows this structure:
- **Domain**: Entities, value objects, domain events, repository interfaces
- **Application**: Commands, queries, handlers, DTOs, integration events, `IUnitOfWork`
- **Infrastructure**: `ModuleDbContext`, migrations, repository implementations, module composition root
- **Presentation**: Minimal API endpoints (REPR pattern)

All modules are auto-discovered via `AddModulus<TradeFlowHostModule>()` and share:
- Multi-tenancy support via `ICurrentTenant`
- User context via `ICurrentUser`
- Domain event dispatching via `DomainEventDispatcher`
- Audit trails via `ModuleDbContext`
- Snake_case naming conventions via `EFCore.NamingConventions`

```
src/
├── API/
│   └── TradeFlow.Api/          # Composition root, middleware, startup
├── Shared/                        # Shared kernel (framework-level utilities)
│   ├── TradeFlow.Shared.Domain/
│   ├── TradeFlow.Shared.Application/
│   ├── TradeFlow.Shared.Infrastructure/
│   └── TradeFlow.Shared.Presentation/
└── Modules/
    ├── Configuration/             # Application configuration management
    ├── Identity/                  # User authentication, authorization, session management
    ├── Tenants/                   # Multi-tenant organization management
    ├── Notifications/             # User notification system
    ├── Vendors/                   # Vendor registration, classification, performance
    ├── Budgeting/                 # Budget management, control, real-time checks
    ├── Customs/                   # Duty/tax calculation, regime selection
    ├── Procurement/               # Requisitions, POs, sourcing
    ├── TradeFinance/              # LC/TT management, drawdown, expiry
    ├── Import/                    # Import files, documents, state machine
    ├── Inventory/                 # Stock receipt, weighted average, FEFO, GRN tolerance
    └── Costing/                   # Landed cost allocation (banker's rounding)
```

### Business Modules (BRS Phase 1)

All 8 business modules implement the **4-layer Clean Architecture** pattern with complete separation of concerns:

#### **Vendors Module**
- Vendor registration and lifecycle management
- Classification (Manufacturer, Trader, ServiceProvider, OverseasAgent)
- Performance tracking and KPIs
- Bank account and compliance records

#### **Budgeting Module**
- Budget management with control modes (Soft/Hard)
- Real-time commitment/reserve tracking
- Budget availability checks for POs
- Budget period and fiscal year management

#### **Customs Module**
- Duty/tax calculation based on HS codes and regimes
- Multiple regime support (General, ATA, EPZ, Bonded)
- Regulatory authority (NBR, BEZA) integration seams
- Preferential treatment eligibility

#### **Procurement Module**
- Purchase requisitions with approval workflow
- Purchase orders from PRs, contracts, or manual
- Feasibility snapshot at PO submission
- Revision tracking and value-increasing re-approval

#### **TradeFinance Module**
- Letter of Credit (LC) management (irrevocable, confirmed, revolving)
- Telegraphic Transfer (TT) with payment schedules
- Expiry tracking and drawdown management
- Banking partner integration seams

#### **Import Module**
- Import file lifecycle (Draft → Instrumented → Shipped → Cleared → Closed)
- Document management (Proforma Invoice, LC/TT, BL, Insurance)
- State machine with event sourcing
- Port of loading/discharge tracking

#### **Inventory Module**
- Stock receipt and issue at warehouse level
- Weighted average cost calculation on GRN
- FEFO (First Expired, First Out) batch suggestion
- Over-receipt tolerance handling (default 10%)

#### **Costing Module**
- Landed cost allocation with multiple drivers (value, quantity, selected lines)
- Banker's rounding at 4 decimal places
- Residual penny distribution to largest line
- FX conversion support
- Finalized cost sheet locking

## Running it

This app uses a **single shared SQLite database** for development (per-module schemas supported but not required). For production, switch to PostgreSQL.

```bash
# Development (SQLite)
dotnet restore TradeFlow.slnx
dotnet build TradeFlow.slnx
dotnet run --project src/API/TradeFlow.Api -- --seed   # migrate + seed sample data
dotnet run --project src/API/TradeFlow.Api             # runs on http://localhost:5000
```

For production with PostgreSQL, update `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Vendors": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=...",
    "Budgeting": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=...",
    // ... all other modules
  }
}
```

## Configuration

Per-module connection strings are configured in `appsettings.json`. All modules use the same `TradeFlow` database — they keep their tables isolated via per-module PostgreSQL schemas in production, or separate tables in SQLite for development.

```json
{
  "ConnectionStrings": {
    "Vendors": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Budgeting": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Customs": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Procurement": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "TradeFinance": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Import": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Inventory": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow",
    "Costing": "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow"
  }
}
```

## Testing

```bash
# Run all unit tests (compile-only with SAC)
dotnet test TradeFlow.slnx --filter "Category=Unit"

# Run domain tests
dotnet test tests/TradeFlow.DomainTests/TradeFlow.DomainTests.csproj

# Run integration tests (requires SAC bypass)
dotnet test tests/TradeFlow.IntegrationTests/TradeFlow.IntegrationTests.csproj
```

## Features

### Modulus Framework Integration

This sample demonstrates integration with Modulus framework packages (all published under the
`Cobytelabs.Modulus.*` NuGet namespace):

- **Module System**: `Cobytelabs.Modulus.Core` for auto-discovery and dependency management
- **Mediator**: `Cobytelabs.Modulus.Mediator` for command/query handling
- **Data Access**: `Npgsql.EntityFrameworkCore.PostgreSQL` + EF Core, following Modulus's module-per-`DbContext` convention
- **Identity**: `Cobytelabs.Modulus.Identity` for OpenIddict-based auth (password + authorization-code flows)
- **Inbox/Outbox**: `Cobytelabs.Modulus.Inbox` / `Cobytelabs.Modulus.Outbox` for reliable messaging
- **Authorization**: `Cobytelabs.Modulus.Platform` for RBAC + permission-based access control
- **Multi-tenancy**: `Cobytelabs.Modulus.Platform` for tenant isolation
- **Sagas**: `Cobytelabs.Modulus.Sagas` for long-running process orchestration (bring your own Rebus transport)
- **Observability**: `Cobytelabs.Modulus.Observability` for OpenTelemetry integration
- **API features**: `Cobytelabs.Modulus.AspNetCore` for rate limiting, security headers, idempotency

### Production-Ready Features

- **Security**: CORS, rate limiting, security headers, correlation IDs, secrets guard
- **Observability**: OpenTelemetry with OTLP export, distributed tracing, structured logging
- **Health Checks**: Liveness and readiness probes at `/health/live` and `/health/ready`
- **Error Handling**: Global exception handler with RFC 7807 Problem Details
- **Idempotency**: Request deduplication for POST/PATCH operations
- **API Documentation**: Swagger/OpenAPI with versioning at `/openapi/v1.json`
- **Feature Flags**: Microsoft.FeatureManagement with percentage/time-window gates

## BRS Documentation

The authoritative business requirements are in `docs/BRS/`:

| Document | Description |
|----------|-------------|
| [BRS-Core.md](docs/BRS/BRS-Core.md) | Document control, executive summary, goals, actors, lifecycles, MVP scope, NFRs |
| [BRS-Business-Rules.md](docs/BRS/BRS-Business-Rules.md) | Consolidated business-rule register (~150 rules) + deterministic computation reference |
| [BRS-Phasing-Implementation.md](docs/BRS/BRS-Phasing-Implementation.md) | Phase 1 deliverables & exit criteria, Modulus module mapping, testing strategy |

## Endpoints

### Platform Modules
- `POST /api/auth/register` — User registration
- `POST /api/auth/login` — User login (returns JWT)
- `GET /api/tenants` — List tenants
- `GET /api/notifications` — List user notifications

### Business Modules
- **Vendors**: `GET /vendors`, `GET /vendors/{id}`, `POST /vendors`
- **Budgeting**: `GET /budgets`, `GET /budgets/{id}`, `POST /budgets`
- **Customs**: `GET /customs/duty-calculate`, `GET /customs/ regimes`
- **Procurement**: `GET /prs`, `GET /prs/{id}`, `POST /prs`, `GET /pos`, `GET /pos/{id}`, `POST /pos`
- **TradeFinance**: `GET /lcs`, `GET /lcs/{id}`, `POST /lcs`, `GET /tts`, `GET /tts/{id}`, `POST /tts`
- **Import**: `GET /import-files`, `GET /import-files/{id}`, `POST /import-files`, `POST /import-files/{id}/advance`
- **Inventory**: `GET /inventory/stock`, `POST /inventory/stock/receive`, `POST /inventory/stock/issue`
- **Costing**: `GET /costing/sheets`, `GET /costing/sheets/{id}`, `POST /costing/sheets`, `POST /costing/sheets/{id}/finalize`

## Customization

This is designed as a **modular template** that can be adapted for any business domain:

1. **Use the platform modules** as-is for identity, tenants, and notifications
2. **Add business modules** following the 4-layer pattern (Domain → Application → Infrastructure → Presentation)
3. **Configure external services** via `appsettings.json` and per-module settings
4. **Extend permissions** in `TradeFlowHostModule.cs` for your specific domain requirements
5. **Customize endpoints** by adding new REPR endpoints in Presentation layers
6. **Run migrations** per module: `dotnet ef migrations add {Name} --project {Module}.Infrastructure`

## License

MIT