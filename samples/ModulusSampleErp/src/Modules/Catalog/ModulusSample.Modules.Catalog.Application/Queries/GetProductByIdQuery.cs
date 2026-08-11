using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Dtos;

namespace ModulusSample.Modules.Catalog.Application.Queries;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;
