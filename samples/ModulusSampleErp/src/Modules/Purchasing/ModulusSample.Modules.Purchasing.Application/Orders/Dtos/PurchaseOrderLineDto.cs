namespace ModulusSample.Modules.Purchasing.Application.Orders.Dtos;

public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal ReceivedQuantity);