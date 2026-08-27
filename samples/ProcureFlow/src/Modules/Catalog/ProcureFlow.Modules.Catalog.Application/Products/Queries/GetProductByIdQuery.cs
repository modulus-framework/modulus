using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Products.Dtos;

namespace ModulusSample.Modules.Catalog.Application.Products.Queries;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto?>;