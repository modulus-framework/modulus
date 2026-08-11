using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Inventory.Application.Dtos;

namespace ModulusSample.Modules.Inventory.Application.Queries;

public sealed record GetWarehouseByIdQuery(Guid Id) : IQuery<WarehouseDto?>;
