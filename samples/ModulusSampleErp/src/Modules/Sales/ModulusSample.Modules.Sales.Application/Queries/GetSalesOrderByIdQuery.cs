using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Dtos;

namespace ModulusSample.Modules.Sales.Application.Queries;

public sealed record GetSalesOrderByIdQuery(Guid Id) : IQuery<SalesOrderDto?>;
