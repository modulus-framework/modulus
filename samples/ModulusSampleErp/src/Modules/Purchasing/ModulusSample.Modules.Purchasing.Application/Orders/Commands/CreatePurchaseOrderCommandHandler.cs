using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Commands;

public sealed class CreatePurchaseOrderCommandHandler(
    IRequisitionRepository requisitionRepository,
    IPurchaseOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreatePurchaseOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var requisition = await requisitionRepository.GetByIdAsync(request.RequisitionId, cancellationToken);

        if (requisition is null)
            return Result.Failure<Guid>(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var orderId = Guid.NewGuid();

        var result = PurchaseOrder.Create(
            orderId,
            request.OrderNumber,
            request.RequisitionId,
            request.SupplierId,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await orderRepository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(orderId);
    }
}