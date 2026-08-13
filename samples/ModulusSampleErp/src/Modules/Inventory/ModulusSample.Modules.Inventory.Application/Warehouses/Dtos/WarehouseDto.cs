namespace ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;

public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string City,
    string PostalCode,
    string Country,
    Guid OrgUnitId,
    Guid TenantId,
    bool IsActive);