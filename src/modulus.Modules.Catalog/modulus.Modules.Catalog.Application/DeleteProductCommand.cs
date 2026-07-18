using Modulus.Mediator.Abstractions;
using Modulus.Core.Abstractions.Common;

namespace modulus.Modules.Catalog.Application;

public sealed record DeleteProductCommand(Guid Id) : ICommand<Unit>;
