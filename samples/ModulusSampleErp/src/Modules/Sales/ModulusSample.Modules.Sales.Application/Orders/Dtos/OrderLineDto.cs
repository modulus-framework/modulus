namespace ModulusSample.Modules.Sales.Application.Orders.Dtos;

public sealed record OrderLineDto(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);