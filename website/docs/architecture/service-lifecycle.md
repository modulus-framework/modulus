---
sidebar_position: 3
---

# Service Lifecycle

Modulus modules go through a well-defined lifecycle from discovery to shutdown.

## Startup Phases

### 1. Module Registration

When `AddModulus(configuration, modules => ...)` is called, the framework:

1. Instantiates each module registered via `AddModule<T>()` (exactly once)
2. Registers every module instance in DI
3. Preserves the registration order as the authoritative lifecycle order

### 2. PreConfigureServices

Runs before main configuration. Use this to:

- Seed shared option registries
- Register permission definitions
- Set up global conventions

```csharp
public override void PreConfigureServices(IServiceCollection services, IConfiguration config)
{
    // Runs BEFORE ConfigureServices for any module
}
```

### 3. ConfigureServices

The main service registration phase. Each module registers its:

- DbContext and repositories
- Mediator handlers
- Background jobs
- Module-specific services

```csharp
public override void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    services.AddModuleDatabase<CatalogDbContext>(config);
    services.AddMediatorHandlers(typeof(CatalogModule).Assembly);
}
```

### 4. PostConfigureServices

Runs after all modules have registered services. Use this to:

- Freeze registries
- Build consolidated maps
- Validate that required services are registered

```csharp
public override void PostConfigureServices(IServiceCollection services, IConfiguration config)
{
    // Runs AFTER ConfigureServices for every module
}
```

### 5. InitializeAsync

Runs after the host builds. Use this for:

- Database migrations
- Data seeding
- Health check registration

```csharp
public override async Task InitializeAsync(ModuleContext context)
{
    var db = context.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
}
```

## Shutdown

### ShutdownAsync

Runs in **reverse registration order** when the application stops:

```csharp
public override async Task ShutdownAsync()
{
    // Cleanup resources
    // Flush buffers
    // Close connections
}
```

## Execution Order Example

Given this registration in `Program.cs`:

```csharp
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<IdentityModule>()
    .AddModule<CatalogModule>()
    .AddModule<OrdersModule>()
    .AddModule<InventoryModule>());
```

The execution order is:

| Phase | Order |
|-------|-------|
| PreConfigureServices | Identity → Catalog → Orders → Inventory |
| ConfigureServices | Identity → Catalog → Orders → Inventory |
| PostConfigureServices | Identity → Catalog → Orders → Inventory |
| InitializeAsync | Identity → Catalog → Orders → Inventory |
| ShutdownAsync | Inventory → Orders → Catalog → Identity |

## Module Context

`InitializeAsync` receives a `ModuleContext` with access to:

```csharp
public override async Task InitializeAsync(ModuleContext context)
{
    // Service provider for resolving services
    var services = context.ServiceProvider;

    // Module configuration
    var config = context.Configuration;

    // Cancellation token
    var ct = context.CancellationToken;
}
```

## Error Handling

If a module's `InitializeAsync` throws:

1. The exception propagates to the host
2. The application fails to start
3. No modules are initialized (partial initialization is not supported)

This ensures consistent startup state.
