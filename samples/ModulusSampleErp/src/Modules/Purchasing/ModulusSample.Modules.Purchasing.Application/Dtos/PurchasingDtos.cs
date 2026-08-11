namespace ModulusSample.Modules.Purchasing.Application.Dtos;

public sealed record PurchaseRequisitionDto(
    Guid Id,
    string RequisitionNumber,
    Guid RequesterId,
    Guid? ApproverId,
    decimal TotalAmount,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);

public sealed record RequisitionLineDto(
    Guid Id,
    Guid SupplierId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record PurchaseOrderDto(
    Guid Id,
    string OrderNumber,
    Guid RequisitionId,
    Guid SupplierId,
    decimal TotalAmount,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);

public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal ReceivedQuantity);

public sealed record GoodsReceiptDto(
    Guid Id,
    string ReceiptNumber,
    Guid PurchaseOrderId,
    DateTime ReceivedDate,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);

public sealed record ReceiptLineDto(
    Guid Id,
    Guid ProductId,
    decimal QuantityReceived,
    string LotNumber);
