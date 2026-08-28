---
sidebar_position: 1
---

# Samples

## ProcureFlow

A reference application demonstrating the framework's recommended shape with 18 modules covering procurement, import management, trade finance, inventory, and workflow.

### Structure

```
samples/ProcureFlow/
├── ProcureFlow.slnx
├── src/
│   ├── API/                            # Host API
│   ├── Shared/                         # Shared kernel
│   └── Modules/                        # 18 business modules
│       ├── Budgeting/
│       ├── Configuration/
│       ├── Costing/
│       ├── Customs/
│       ├── Features/
│       ├── Finance/
│       ├── Identity/
│       ├── Import/
│       ├── Inventory/
│       ├── Notifications/
│       ├── OrgStructure/
│       ├── Procurement/
│       ├── SpendAnalysis/
│       ├── Tenants/
│       ├── TradeFinance/
│       ├── Vendors/
│       ├── VirtualFileExplorer/
│       └── WorkflowEngine/
└── tests/
```

### Running

```bash
cd samples/ProcureFlow
dotnet run --project src/API/ProcureFlow.Api
```

### Features Demonstrated

- Per-module databases (SQLite)
- CQRS with mediator
- Transactional outbox
- Multi-tenancy
- Authentication (OpenIddict)
- Health checks
- API versioning

## Creating a Sample

```bash
# Install the CLI
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli

# Create a new app
modulus app MyApp

# Add modules
modulus add-module Catalog
modulus add-module Orders

# Generate CRUD
modulus generate-crud Product --module Catalog
modulus generate-crud Order --module Orders

# Run
cd src/API/MyApp.Api
dotnet run
```

## See Also

- [Quick Start](getting-started/quick-start) — Your first Modulus app
