namespace ModulusSample.Modules.Catalog.Application.IntegrationEvents;

public sealed record ProductCreatedIntegrationEvent(Guid ProductId, string ProductSku, string Name, decimal Price, string Category, DateTime CreatedAtUtc);
public sealed record ProductPriceChangedIntegrationEvent(Guid ProductId, string ProductSku, string Name, decimal OldPrice, decimal NewPrice, DateTime ChangedAtUtc);
public sealed record ProductStockUpdatedIntegrationEvent(Guid ProductId, string ProductSku, int CurrentStock, DateTime UpdatedAtUtc);
public sealed record ProductDiscontinuedIntegrationEvent(Guid ProductId, string ProductSku, string Name, DateTime DiscontinuedAtUtc);
public sealed record CategoryCreatedIntegrationEvent(Guid CategoryId, string Name, string ParentCategoryId, DateTime CreatedAtUtc);
public sealed record CategoryUpdatedIntegrationEvent(Guid CategoryId, string Name, string ParentCategoryId, DateTime UpdatedAtUtc);