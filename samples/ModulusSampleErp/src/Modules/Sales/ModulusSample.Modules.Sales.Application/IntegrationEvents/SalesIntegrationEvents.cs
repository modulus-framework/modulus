namespace ModulusSample.Modules.Sales.Application.IntegrationEvents;

public sealed record OrderCreatedIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, decimal TotalAmount, DateTime CreatedAtUtc);
public sealed record OrderConfirmedIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, decimal TotalAmount, DateTime ConfirmedAtUtc);
public sealed record OrderShippedIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, string TrackingNumber, DateTime ShippedAtUtc);
public sealed record OrderDeliveredIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, DateTime DeliveredAtUtc);
public sealed record OrderCancelledIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, string Reason, DateTime CancelledAtUtc);
public sealed record OrderRefundedIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId, decimal RefundAmount, DateTime RefundedAtUtc);
public sealed record CustomerOrderPlacedIntegrationEvent(Guid CustomerId, Guid OrderId, string OrderNumber, decimal TotalAmount, DateTime PlacedAtUtc);
public sealed record CustomerOrderCompletedIntegrationEvent(Guid CustomerId, Guid OrderId, string OrderNumber, decimal TotalAmount, DateTime CompletedAtUtc);