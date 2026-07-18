using Modulus.Mediator.Abstractions;
using modulus.Modules.Catalog.Contracts.Dtos;

namespace modulus.Modules.Catalog.Application;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;
