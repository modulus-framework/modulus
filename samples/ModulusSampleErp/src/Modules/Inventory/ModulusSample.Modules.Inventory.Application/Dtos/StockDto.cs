namespace ModulusSample.Modules.Inventory.Application.Dtos;

public sealed record StockDto(
    Guid Id,
    Guid ProductId,
    Guid WarehouseId,
    int QuantityOnHand,
    int ReorderPoint,
    int ReorderQuantity,
    Guid TenantId,
    DateTime CreatedAt);
