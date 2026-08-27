namespace ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;

public sealed record GoodsReceiptDto(
    Guid Id,
    string ReceiptNumber,
    Guid PurchaseOrderId,
    DateTime ReceivedDate,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);