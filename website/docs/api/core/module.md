---
sidebar_position: 2
---

# Module API

## IModule

The primary contract for all Modulus modules.

```csharp
public interface IModule
{
    void PreConfigureServices(IServiceCollection services, IConfiguration config);
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    void PostConfigureServices(IServiceCollection services, IConfiguration config);
    Task InitializeAsync(ModuleContext context, CancellationToken cancellationToken = default);
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
public class ModuleContext
{
    public IServiceProvider ServiceProvider { get; }
    public IConfiguration Configuration { get; }
    public CancellationToken CancellationToken { get; }
}
```

## See Also

- [Module System](/docs/architecture/module-system) — Module concepts
