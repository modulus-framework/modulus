namespace ModulusSample.Modules.Sales.Application.Orders.Dtos;

public sealed record SalesOrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    Guid OrgUnitId,
    Guid TenantId);