using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Sales.Application.Orders.Dtos;

namespace ModulusSample.Modules.Sales.Application.Orders.Queries;

public sealed record GetSalesOrderByIdQuery(Guid Id) : IQuery<SalesOrderDto?>;