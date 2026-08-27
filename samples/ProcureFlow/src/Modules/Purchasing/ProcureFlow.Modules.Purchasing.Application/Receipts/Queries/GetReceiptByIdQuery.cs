using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Queries;

public sealed record GetReceiptByIdQuery(Guid Id) : IQuery<GoodsReceiptDto?>;