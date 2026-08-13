using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed class CreateGoodsReceiptCommandHandler(
    IPurchaseOrderRepository orderRepository,
    IGoodsReceiptRepository receiptRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateGoodsReceiptCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateGoodsReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.PurchaseOrderId, cancellationToken);

        if (order is null)
            return Result.Failure<Guid>(Error.NotFound("Order.NotFound", "Purchase order not found"));

        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var receiptId = Guid.NewGuid();

        var result = GoodsReceipt.Create(
            receiptId,
            request.ReceiptNumber,
            request.PurchaseOrderId,
            DateTime.UtcNow,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await receiptRepository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(receiptId);
    }
}