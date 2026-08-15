namespace ModulusSample.Modules.Inventory.Application.IntegrationEvents;

public sealed record StockAddedIntegrationEvent(Guid ProductId, string ProductSku, string LocationCode, int Quantity, DateTime AddedAtUtc);
public sealed record StockRemovedIntegrationEvent(Guid ProductId, string ProductSku, string LocationCode, int Quantity, DateTime RemovedAtUtc);
public sealed record StockTransferredIntegrationEvent(Guid ProductId, string ProductSku, string SourceLocation, string DestinationLocation, int Quantity, DateTime TransferredAtUtc);
public sealed record LowStockAlertIntegrationEvent(Guid ProductId, string ProductSku, string LocationCode, int CurrentQuantity, int Threshold, DateTime AlertAtUtc);
public sealed record StockReservedIntegrationEvent(Guid ProductId, string ProductSku, int Quantity, string ReferenceType, string ReferenceId, DateTime ReservedAtUtc);
public sealed record StockReleasedIntegrationEvent(Guid ProductId, string ProductSku, int Quantity, string ReferenceType, string ReferenceId, DateTime ReleasedAtUtc);