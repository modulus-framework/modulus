namespace ModulusSample.Modules.Sales.Application.Dtos;

public sealed record SalesOrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    Guid OrgUnitId,
    Guid TenantId);

public sealed record OrderLineDto(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
