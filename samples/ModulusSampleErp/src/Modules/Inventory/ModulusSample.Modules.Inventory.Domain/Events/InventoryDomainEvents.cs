namespace ModulusSample.Modules.Inventory.Domain.Events;

public sealed record StockAddedDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int Quantity, DateTime AddedAtUtc);
public sealed record StockRemovedDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int Quantity, DateTime RemovedAtUtc);
public sealed record StockTransferredDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string SourceLocation, string DestinationLocation, int Quantity, DateTime TransferredAtUtc);
public sealed record StockAdjustedDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int OldQuantity, int NewQuantity, string Reason, DateTime AdjustedAtUtc);
public sealed record LowStockAlertDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int CurrentQuantity, int Threshold, DateTime AlertAtUtc);
public sealed record StockReservedDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int Quantity, string ReferenceType, string ReferenceId, DateTime ReservedAtUtc);
public sealed record StockReleasedDomainEvent(Guid EventId, Guid StockId, Guid ProductId, string ProductSku, string LocationCode, int Quantity, string ReferenceType, string ReferenceId, DateTime ReleasedAtUtc);