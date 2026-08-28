---
sidebar_position: 2
---

# Module API

## IModule

The primary contract for all Modulus modules.

```csharp
public interface IModule
{
    IReadOnlyList<Type> DependsOn { get; }
    void PreConfigureServices(IServiceCollection services, IConfiguration config);
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    void PostConfigureServices(IServiceCollection services, IConfiguration config);
    Task InitializeAsync(ModuleContext context);
    Task ShutdownAsync();
}
```

## ModulusModule

Convenience base class with no-op defaults.

```csharp
public abstract class ModulusModule : IModule
{
    public virtual IReadOnlyList<Type> DependsOn => [];
    public virtual void PreConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual void ConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual void PostConfigureServices(IServiceCollection s, IConfiguration c) { }
    public virtual Task InitializeAsync(ModuleContext context) => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;
}
```

## [DependsOn]

Declares module dependencies.

```csharp
[DependsOn(typeof(IdentityModule))]
[DependsOn(typeof(ReportingModule), Optional = true)]
public sealed class CatalogModule : ModulusModule { }
```

## ModulusBuilder

Fluent builder for `AddModulus()`.

```csharp
builder.Services.AddModulus<HostModule>(builder.Configuration);
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
