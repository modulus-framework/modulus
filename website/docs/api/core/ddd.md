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
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
```

## IAuditableEntity

```csharp
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
```

## IHasTenantId

```csharp
public interface IHasTenantId
{
    Guid TenantId { get; set; }   // non-nullable; stamped automatically
}
```

## [ProtectedPersonalData]

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProtectedPersonalDataAttribute : Attribute { }
```

Mark a `string` property to encrypt it at rest (see
[Personal Data Protection](../../hardening/personal-data-protection)).

## PagedList\<T\>

```csharp
public sealed record PagedList<T>
{
    public IReadOnlyList<T> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
    public int TotalPages { get; }
    public PagedList<TResult> Map<TResult>(Func<T, TResult> selector);
}
```
