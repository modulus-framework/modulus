using Modulus.Mediator.Abstractions;

namespace modulus.Modules.Catalog.Application;

public sealed record CreateProductCommand(string Name) : ICommand<Guid>;
