using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Queries;

public sealed record ListOrdersQuery(int Page, int PageSize) : IQuery<PagedResult<PurchaseOrderDto>>;