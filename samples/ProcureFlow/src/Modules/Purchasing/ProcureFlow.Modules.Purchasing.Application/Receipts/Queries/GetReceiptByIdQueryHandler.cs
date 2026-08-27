using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Queries;

public sealed class GetReceiptByIdQueryHandler(
    IGoodsReceiptRepository repository) : IQueryHandler<GetReceiptByIdQuery, GoodsReceiptDto?>
{
    public async Task<GoodsReceiptDto?> HandleAsync(
        GetReceiptByIdQuery request,
        CancellationToken cancellationToken)
    {
        var receipt = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (receipt is null)
            return null;

        return new GoodsReceiptDto(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.PurchaseOrderId,
            receipt.ReceivedDate,
            receipt.Status,
            receipt.OrgUnitId,
            receipt.TenantId);
    }
}