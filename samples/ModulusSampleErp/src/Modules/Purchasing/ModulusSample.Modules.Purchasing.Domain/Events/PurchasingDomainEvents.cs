namespace ModulusSample.Modules.Purchasing.Domain.Events;

public sealed record PurchaseOrderCreatedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, decimal TotalAmount, DateTime CreatedAtUtc);
public sealed record PurchaseOrderSubmittedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, decimal TotalAmount, DateTime SubmittedAtUtc);
public sealed record PurchaseOrderApprovedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, string ApprovedBy, DateTime ApprovedAtUtc);
public sealed record PurchaseOrderRejectedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, string Reason, string RejectedBy, DateTime RejectedAtUtc);
public sealed record PurchaseOrderReceivedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, DateTime ReceivedAtUtc);
public sealed record PurchaseOrderPartiallyReceivedDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, decimal ReceivedAmount, DateTime PartiallyReceivedAtUtc);
public sealed record PurchaseOrderCancelledDomainEvent(Guid EventId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid SupplierId, string Reason, DateTime CancelledAtUtc);
public sealed record RequisitionCreatedDomainEvent(Guid EventId, Guid RequisitionId, string RequisitionNumber, string RequesterId, string Department, DateTime CreatedAtUtc);
public sealed record RequisitionSubmittedDomainEvent(Guid EventId, Guid RequisitionId, string RequisitionNumber, string RequesterId, string Department, DateTime SubmittedAtUtc);
public sealed record RequisitionApprovedDomainEvent(Guid EventId, Guid RequisitionId, string RequisitionNumber, string RequesterId, string Department, string ApprovedBy, DateTime ApprovedAtUtc);
public sealed record RequisitionRejectedDomainEvent(Guid EventId, Guid RequisitionId, string RequisitionNumber, string RequesterId, string Department, string Reason, string RejectedBy, DateTime RejectedAtUtc);
public sealed record RequisitionProcessedDomainEvent(Guid EventId, Guid RequisitionId, string RequisitionNumber, string RequesterId, string Department, DateTime ProcessedAtUtc);