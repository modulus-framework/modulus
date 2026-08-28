---
sidebar_position: 2
---

# Module System

Modules are the fundamental building blocks of a Modulus application. Each module encapsulates a business domain with its own data, logic, and API surface.

## Module Declaration

```csharp
[DependsOn(typeof(IdentityModule), typeof(DataModule))]
public sealed class CatalogModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Register module-specific services
        services.AddModuleDatabase<CatalogDbContext>(config);
        services.AddMediatorHandlers(typeof(CatalogModule).Assembly);
    }
}
```

## Module Discovery

Modules are discovered via the `[DependsOn]` attribute. The framework builds a dependency graph and runs services in topological order:

```csharp
// Program.cs
builder.Services.AddModulus<HostModule>(builder.Configuration);
```

This automatically discovers all modules and configures services in three phases:

| Phase | Purpose | Example |
|-------|---------|---------|
| `PreConfigureServices` | Seed shared options/registries | Register permission definitions |
| `ConfigureServices` | Main service registration | Register DbContext, repositories, handlers |
| `PostConfigureServices` | Finalize after all modules registered | Freeze registries, build consolidated maps |

## Dependency Declaration

```csharp
// Simple dependency
[DependsOn(typeof(IdentityModule))]

// Optional dependency (module works without it)
[DependsOn(typeof(ReportingModule), Optional = true)]
```

## Module Lifecycle

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Startup                       │
├─────────────────────────────────────────────────────────────┤
│  1. Module Discovery (topological sort)                     │
│  2. PreConfigureServices (per module, in dependency order)  │
│  3. ConfigureServices (per module, in dependency order)     │
│  4. PostConfigureServices (per module, in dependency order) │
│  5. Host Build                                              │
│  6. InitializeAsync (per module, in dependency order)       │
├─────────────────────────────────────────────────────────────┤
│                    Application Shutdown                      │
├─────────────────────────────────────────────────────────────┤
│  7. ShutdownAsync (per module, REVERSE dependency order)    │
└─────────────────────────────────────────────────────────────┘
```

## Module Registration

Each module registers its own services in `ConfigureServices`:

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    // Database
    services.AddModuleDatabase<CatalogDbContext>(config);

    // Unit of Work
    services.AddScoped<IUnitOfWork>(sp =>
        sp.GetRequiredService<CatalogDbContext>());

    // Mediator handlers
    services.AddMediatorHandlers(typeof(CatalogModule).Assembly);

    // Module-specific services
    services.AddScoped<IProductRepository, ProductRepository>();
}
```

## Host Module

The host module declares dependencies on all business modules:

```csharp
[DependsOn(
    typeof(CatalogModule),
    typeof(OrdersModule),
    typeof(InventoryModule),
    typeof(IdentityModule)
)]
public sealed class HostModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Host-level configuration only
    }
}
```

## Cross-Module Communication

Modules communicate through:

1. **Direct dependencies** — `[DependsOn]` + inject services from other modules
2. **Integration events** — Publish events that other modules subscribe to
3. **Shared kernel** — Common abstractions in `Modulus.Core`

```csharp
// Module A publishes an event
public sealed class ProductCreatedEvent : IIntegrationEvent
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = default!;
}

// Module B handles the event
public sealed class ProductCreatedHandler
    : IIntegrationEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent @event)
    {
        // React to product creation
    }
}
```

## See Also

- [Service Lifecycle](service-lifecycle) — Phase details
- [Clean Architecture](clean-architecture) — Per-module layer structure
