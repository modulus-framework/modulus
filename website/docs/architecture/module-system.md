---
sidebar_position: 2
---

# Module System

Modules are the fundamental building blocks of a Modulus application. Each module encapsulates a business domain with its own data, logic, and API surface.

## Module Declaration

```csharp
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

## Module Registration

Modules are registered **explicitly** in `Program.cs` — there is no
dependency-attribute discovery and no startup module. `AddModulus(configuration,
configure)` invokes a `ModulusBuilder` callback where each module is added in
order. **Registration order is authoritative**: it is the order in which every
lifecycle phase runs, so register a module *before* the modules that rely on its
services.

```csharp
// Program.cs
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<IdentityModule>()
    .AddModule<CatalogModule>()
    .AddModule<OrdersModule>()
    .AddModule<InventoryModule>());
```

Services are then configured in three phases (each phase runs across **all**
modules, in registration order):

| Phase | Purpose | Example |
|-------|---------|---------|
| `PreConfigureServices` | Seed shared options/registries | Register permission definitions |
| `ConfigureServices` | Main service registration | Register DbContext, repositories, handlers |
| `PostConfigureServices` | Finalize after all modules registered | Freeze registries, build consolidated maps |

## Module Lifecycle

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Startup                       │
├─────────────────────────────────────────────────────────────┤
│  1. Explicit registration (AddModulus callback)             │
│  2. PreConfigureServices (per module, in registration order)│
│  3. ConfigureServices (per module, in registration order)   │
│  4. PostConfigureServices (per module, in registration order)│
│  5. Host Build                                              │
│  6. InitializeAsync (per module, in registration order)     │
├─────────────────────────────────────────────────────────────┤
│                    Application Shutdown                      │
├─────────────────────────────────────────────────────────────┤
│  7. ShutdownAsync (per module, REVERSE registration order)  │
└─────────────────────────────────────────────────────────────┘
```

## Service Registration

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

## Cross-Module Communication

Modules communicate through:

1. **Shared services** — register in an earlier module, consume from a later one (registration order guarantees the provider is configured first)
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
