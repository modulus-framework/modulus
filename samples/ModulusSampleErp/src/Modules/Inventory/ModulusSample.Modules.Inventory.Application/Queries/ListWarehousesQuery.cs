using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Application.Queries;

public sealed record ListWarehousesQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedResult<WarehouseDto>>;
