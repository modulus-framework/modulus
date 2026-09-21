---
sidebar_position: 4
---

# Abstractions

## ICurrentTenant

```csharp
public interface ICurrentTenant
{
    Guid? TenantId { get; }
    string? TenantSlug { get; }
    bool IsAvailable { get; }
    bool IsHost { get; }
    IDisposable Change(TenantInfo? tenant);
}
```

`IsHost` is true only when multi-tenancy is off or inside an explicit
`Change(null)` scope — unresolved tenants stay fail-closed. See
[Multi-Tenancy](../../platform/multi-tenancy).

## ICurrentUser

```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    IReadOnlyList<string> Permissions { get; }
}
```

## ICorrelationContext

```csharp
public interface ICorrelationContext
{
    string? CorrelationId { get; }
    bool IsSet { get; }
    IDisposable BeginScope(string correlationId);
}
```

## IPermissionRegistry

```csharp
public interface IPermissionRegistry
{
    void Add(string permission, string description, string[]? requires = null);
    IReadOnlyList<PermissionDefinition> GetAll();
    IReadOnlyList<PermissionDefinition> GetByModule(string module);
    bool Exists(string permission);
    void Freeze();
}
```

Declare via `services.AddPermissions("Module", registry => …)`; the registry
freezes after startup.

## IFeatureGate

Sync entitlement gate ("is this capability available to the current tenant at
all?") — distinct from `IFeatureManager` rollout evaluation and from
per-user `HasPermission`:

```csharp
public interface IFeatureGate
{
    bool IsEnabled(string feature);
}
```

Fail-closed once feature management is configured; unconfigured it falls back
to everything-enabled. Gated endpoints require the entitlement **and** the
rollout.

## ModulusException

```csharp
public abstract class ModulusException(string message, Exception? inner = null)
    : Exception(message, inner);
```

Mapped to HTTP status by the global exception handler: `ValidationException`
→ 400, `NotFoundException` → 404, `UnauthorizedException` → 401,
`ForbiddenException` → 403, `ConflictException` / concurrency → 409,
`FeatureDisabledException` → 404, else 500.

## NotFoundException

```csharp
public sealed class NotFoundException(string message)
    : ModulusException(message);

// Usage
throw new NotFoundException($"Product {id} was not found.");
```

## See Also

- [Module API](module) — Module system
- [DDD Primitives](ddd) — Domain types
