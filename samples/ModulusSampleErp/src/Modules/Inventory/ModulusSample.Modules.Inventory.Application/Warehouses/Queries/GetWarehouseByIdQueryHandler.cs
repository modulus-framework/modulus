using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;
using ModulusSample.Modules.Inventory.Domain.Repositories;

namespace ModulusSample.Modules.Inventory.Application.Warehouses.Queries;

public sealed class GetWarehouseByIdQueryHandler(IWarehouseRepository repository)
    : IQueryHandler<GetWarehouseByIdQuery, WarehouseDto?>
{
    public async Task<WarehouseDto?> HandleAsync(GetWarehouseByIdQuery request, CancellationToken cancellationToken)
    {
        var warehouse = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (warehouse is null)
            return null;

        return new WarehouseDto(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.Address,
            warehouse.City,
            warehouse.PostalCode,
            warehouse.Country,
            warehouse.OrgUnitId,
            warehouse.TenantId,
            warehouse.IsActive);
    }
}