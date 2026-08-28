---
sidebar_position: 4
---

# Abstractions

## ICurrentTenant

```csharp
public interface ICurrentTenant
{
    Guid? Id { get; }
    string? Name { get; }
    IDisposable Change(Guid? id, string? name = null);
}
```

## ICurrentUser

```csharp
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Name { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}
```

## ICorrelationContext

```csharp
public interface ICorrelationContext
{
    string Id { get; }
    IDisposable BeginScope(string id);
}
```

## IPermissionRegistry

```csharp
public interface IPermissionRegistry
{
    void Register(string permission, string description);
    IReadOnlyList<PermissionDefinition> GetAll();
}
```

## IFeatureGate

```csharp
public interface IFeatureGate
{
    Task<bool> IsEnabledAsync(string featureName);
}
```

## ModulusException

```csharp
public class ModulusException : Exception
{
    public ModulusException(string message) : base(message) { }
    public ModulusException(string message, Exception inner) : base(message, inner) { }
}
```

## NotFoundException

```csharp
public sealed class NotFoundException : ModulusException
{
    public NotFoundException(string name, object key)
        : base($"Entity \"{name}\" ({key}) was not found.") { }
}
```

## See Also

- [Module API](module) — Module system
- [DDD Primitives](ddd) — Domain types
