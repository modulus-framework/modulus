namespace ModulusSample.Modules.Purchasing.Application.IntegrationEvents;

public sealed record PurchaseOrderCreatedIntegrationEvent(Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, decimal TotalAmount, DateTime CreatedAtUtc);
public sealed record PurchaseOrderApprovedIntegrationEvent(Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, decimal TotalAmount, DateTime ApprovedAtUtc);
public sealed record PurchaseOrderReceivedIntegrationEvent(Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, DateTime ReceivedAtUtc);
public sealed record SupplierPurchaseOrderCreatedIntegrationEvent(Guid SupplierId, Guid PurchaseOrderId, string PurchaseOrderNumber, decimal TotalAmount, DateTime CreatedAtUtc);