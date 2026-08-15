namespace ModulusSample.Modules.Sales.Domain.Events;

public sealed record OrderCreatedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, decimal TotalAmount, DateTime CreatedAtUtc);
public sealed record OrderConfirmedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, decimal TotalAmount, DateTime ConfirmedAtUtc);
public sealed record OrderProcessingDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, DateTime ProcessingAtUtc);
public sealed record OrderShippedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, string TrackingNumber, DateTime ShippedAtUtc);
public sealed record OrderDeliveredDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, DateTime DeliveredAtUtc);
public sealed record OrderCancelledDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, string Reason, DateTime CancelledAtUtc);
public sealed record OrderReturnedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, string Reason, decimal RefundAmount, DateTime ReturnedAtUtc);
public sealed record OrderRefundedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, Guid CustomerId, decimal RefundAmount, DateTime RefundedAtUtc);
public sealed record OrderItemAddedDomainEvent(Guid EventId, Guid OrderId, Guid OrderItemId, Guid ProductId, string ProductSku, int Quantity, decimal UnitPrice, DateTime AddedAtUtc);
public sealed record OrderItemRemovedDomainEvent(Guid EventId, Guid OrderId, Guid OrderItemId, Guid ProductId, string ProductSku, int Quantity, decimal UnitPrice, DateTime RemovedAtUtc);
public sealed record OrderItemQuantityUpdatedDomainEvent(Guid EventId, Guid OrderId, Guid OrderItemId, Guid ProductId, string ProductSku, int OldQuantity, int NewQuantity, DateTime UpdatedAtUtc);
public sealed record OrderPaymentMethodChangedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, string OldPaymentMethod, string NewPaymentMethod, DateTime ChangedAtUtc);
public sealed record OrderShippingAddressChangedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, DateTime ChangedAtUtc);
public sealed record OrderTotalAmountChangedDomainEvent(Guid EventId, Guid OrderId, string OrderNumber, decimal OldTotal, decimal NewTotal, DateTime ChangedAtUtc);