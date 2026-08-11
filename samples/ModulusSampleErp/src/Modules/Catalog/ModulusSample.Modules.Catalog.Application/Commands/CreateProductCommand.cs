using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Application.Commands;

public sealed record CreateProductCommand(
    string Name,
    decimal UnitCost,
    decimal ListPrice,
    string? Description = null,
    Guid? CategoryId = null) : ICommand<Result<Guid>>;
