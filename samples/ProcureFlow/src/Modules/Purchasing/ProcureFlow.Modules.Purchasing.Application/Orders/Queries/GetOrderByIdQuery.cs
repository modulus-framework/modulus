using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Queries;

public sealed record GetOrderByIdQuery(Guid Id) : IQuery<PurchaseOrderDto?>;