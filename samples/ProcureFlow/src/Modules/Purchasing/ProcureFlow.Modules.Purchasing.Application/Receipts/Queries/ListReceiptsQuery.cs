using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Queries;

public sealed record ListReceiptsQuery(int Page, int PageSize) : IQuery<PagedResult<GoodsReceiptDto>>;