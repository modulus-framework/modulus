using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Warehouses.Dtos;

namespace ModulusSample.Modules.Inventory.Application.Warehouses.Queries;

public sealed record GetWarehouseByIdQuery(Guid Id) : IQuery<WarehouseDto?>;