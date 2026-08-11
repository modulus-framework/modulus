using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;
using ModulusSample.Modules.Inventory.Application.Queries;
using ModulusSample.Modules.Inventory.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Infrastructure.Handlers;

internal sealed class ListWarehousesQueryHandler : IQueryHandler<ListWarehousesQuery, PagedResult<WarehouseDto>>
{
    private readonly InventoryDbContext _dbContext;

    public ListWarehousesQueryHandler(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<WarehouseDto>> HandleAsync(ListWarehousesQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        var totalCount = await _dbContext.Warehouses.CountAsync(cancellationToken);

        var warehouses = await _dbContext.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = warehouses.Select(w => new WarehouseDto(
            w.Id,
            w.Code,
            w.Name,
            w.Address,
            w.City,
            w.PostalCode,
            w.Country,
            w.OrgUnitId,
            w.TenantId,
            w.IsActive)).ToList();

        return new PagedResult<WarehouseDto>(items, totalCount, request.Page, request.PageSize);
    }
}
