using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;
using ModulusSample.Modules.Inventory.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Application.Warehouses.Queries;

public sealed class ListWarehousesQueryHandler(IWarehouseRepository repository)
    : IQueryHandler<ListWarehousesQuery, PagedResult<WarehouseDto>>
{
    public async Task<PagedResult<WarehouseDto>> HandleAsync(ListWarehousesQuery request, CancellationToken cancellationToken)
    {
        var warehouses = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = warehouses.Items.Select(w => new WarehouseDto(
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

        return new PagedResult<WarehouseDto>(items, warehouses.TotalCount, request.Page, request.PageSize);
    }
}