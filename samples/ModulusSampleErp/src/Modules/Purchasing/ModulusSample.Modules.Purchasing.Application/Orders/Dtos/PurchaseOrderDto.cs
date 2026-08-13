namespace ModulusSample.Modules.Purchasing.Application.Orders.Dtos;

public sealed record PurchaseOrderDto(
    Guid Id,
    string OrderNumber,
    Guid RequisitionId,
    Guid SupplierId,
    decimal TotalAmount,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);