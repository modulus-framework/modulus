using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed class CreatePurchaseRequisitionCommandHandler(
    IRequisitionRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreatePurchaseRequisitionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreatePurchaseRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var userId = currentUser.UserId ?? Guid.Empty;
        var requisitionId = Guid.NewGuid();

        var result = PurchaseRequisition.Create(
            requisitionId,
            request.RequisitionNumber,
            userId,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(requisitionId);
    }
}