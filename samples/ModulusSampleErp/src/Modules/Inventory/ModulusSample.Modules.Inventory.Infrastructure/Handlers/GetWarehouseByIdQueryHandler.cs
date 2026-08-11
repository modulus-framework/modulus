using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;
using ModulusSample.Modules.Inventory.Application.Queries;
using ModulusSample.Modules.Inventory.Infrastructure.Database;

namespace ModulusSample.Modules.Inventory.Infrastructure.Handlers;

internal sealed class GetWarehouseByIdQueryHandler : IQueryHandler<GetWarehouseByIdQuery, WarehouseDto?>
{
    private readonly InventoryDbContext _dbContext;

    public GetWarehouseByIdQueryHandler(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WarehouseDto?> HandleAsync(GetWarehouseByIdQuery request, CancellationToken cancellationToken)
    {
        var warehouse = await _dbContext.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);

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
