---
sidebar_position: 3
---

# DDD Primitives

## AggregateRoot\<TId\>

Base class for aggregate roots.

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private readonly List<IDomainEvent> _domainEvents = [];

    protected void AddDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents()
        => _domainEvents.Clear();
}
```

## Entity\<TId\>

Base class for entities.

```csharp
public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;
}
```

## ValueObject

Base class for value objects.

```csharp
public abstract class ValueObject
{
    protected abstract IEnumerable<object> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is not ValueObject other) return false;
        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
        => GetEqualityComponents()
            .Aggregate(1, (current, obj) => current * 23 + (obj?.GetHashCode() ?? 0));
}
```

## IDomainEvent

```csharp
public interface IDomainEvent { }
```

## ISoftDelete

```csharp
public interface ISoftDelete
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
    string? DeletedBy { get; }
}
```

## IAuditableEntity

```csharp
public interface IAuditableEntity
{
    DateTime CreatedAt { get; }
    string? CreatedBy { get; }
    DateTime? UpdatedAt { get; }
    string? UpdatedBy { get; }
}
```

## IHasTenantId

```csharp
public interface IHasTenantId
{
    Guid? TenantId { get; }
}
```

## [ProtectedPersonalData]

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class ClassifiedAttribute : Attribute { }
```

## PagedList\<T\>

```csharp
public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int TotalPages { get; }
    public int Page { get; }
    public int PageSize { get; }
}
```
