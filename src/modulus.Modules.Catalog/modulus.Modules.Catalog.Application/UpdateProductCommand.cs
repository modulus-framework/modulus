using Modulus.Mediator.Abstractions;

namespace modulus.Modules.Catalog.Application;

public sealed record UpdateProductCommand(Guid Id, string Name)
    : ICommand<UpdateProductResult>;

public sealed record UpdateProductResult(Guid Id);
