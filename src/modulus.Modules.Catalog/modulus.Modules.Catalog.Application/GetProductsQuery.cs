using Modulus.Mediator.Abstractions;
using modulus.Modules.Catalog.Contracts.Dtos;

namespace modulus.Modules.Catalog.Application;

public sealed record GetProductsQuery : IQuery<IReadOnlyList<ProductDto>>;
