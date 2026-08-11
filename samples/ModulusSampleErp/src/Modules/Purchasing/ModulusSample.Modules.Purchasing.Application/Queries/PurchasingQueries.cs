using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Queries;

public sealed record GetRequisitionByIdQuery(Guid Id) : IQuery<PurchaseRequisitionDto?>;

public sealed record ListRequisitionsQuery(int Page, int PageSize) : IQuery<PagedResult<PurchaseRequisitionDto>>;

public sealed record GetOrderByIdQuery(Guid Id) : IQuery<PurchaseOrderDto?>;

public sealed record ListOrdersQuery(int Page, int PageSize) : IQuery<PagedResult<PurchaseOrderDto>>;

public sealed record GetReceiptByIdQuery(Guid Id) : IQuery<GoodsReceiptDto?>;

public sealed record ListReceiptsQuery(int Page, int PageSize) : IQuery<PagedResult<GoodsReceiptDto>>;
