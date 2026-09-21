---
sidebar_position: 2
---

# Module API

## IModule

The primary contract for all Modulus modules.

```csharp
public interface IModule
{
    // Default no-op: seed shared options/registries other modules contribute to
    void PreConfigureServices(IServiceCollection services, IConfiguration config);

    // Required: register module services
    void ConfigureServices(IServiceCollection services, IConfiguration config);

    // Default no-op: finalize once every module has registered
    void PostConfigureServices(IServiceCollection services, IConfiguration config);

    // Required: migrations, seeding (runs in registration order)
    Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken = default);

    // Required: cleanup (runs in reverse order; only initialized modules)
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
```

## ModulusModule

Convenience base class with no-op defaults.

```csharp
public abstract class ModulusModule : IModule
{
    public virtual void PreConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual void ConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual void PostConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual Task InitializeAsync(ModuleContext context, CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
```

## ModulusBuilder

Fluent builder used inside `AddModulus(configuration, modules => ...)`. Modules
are registered explicitly via `AddModule<TModule>()`; the registration order is
the order every lifecycle phase runs in.

```csharp
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<IdentityModule>()
    .AddModule<CatalogModule>());
```

## ModuleContext

Passed to `InitializeAsync`.

```csharp
public sealed class ModuleContext
{
    public required IServiceProvider ServiceProvider { get; init; }
    public required IConfiguration Configuration { get; init; }
    public required ILogger Logger { get; init; }
    public required ModuleDescriptor Descriptor { get; init; } // Name, ModuleType, InitOrder
}
```

## See Also

- [Module System](/docs/architecture/module-system) — Module concepts
