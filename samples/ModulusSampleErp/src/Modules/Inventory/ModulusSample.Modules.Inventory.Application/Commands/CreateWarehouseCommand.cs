using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Application.Commands;

public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    string Address,
    string City,
    string PostalCode,
    string Country,
    Guid OrgUnitId) : ICommand<Result<Guid>>;
