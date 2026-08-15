namespace ModulusSample.Modules.Catalog.Domain.Events;

public sealed record ProductCreatedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, DateTime CreatedAtUtc);
public sealed record ProductUpdatedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, DateTime UpdatedAtUtc);
public sealed record ProductPriceChangedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, decimal OldPrice, decimal NewPrice, DateTime ChangedAtUtc);
public sealed record ProductActivatedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, DateTime ActivatedAtUtc);
public sealed record ProductDeactivatedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, DateTime DeactivatedAtUtc);
public sealed record ProductDiscontinuedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, DateTime DiscontinuedAtUtc);
public sealed record ProductStockAddedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, int QuantityAdded, DateTime AddedAtUtc);
public sealed record ProductStockRemovedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, int QuantityRemoved, DateTime RemovedAtUtc);
public sealed record ProductCategoryChangedDomainEvent(Guid EventId, Guid ProductId, string ProductSku, string Name, string OldCategory, string NewCategory, DateTime ChangedAtUtc);