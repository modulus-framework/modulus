using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed class ApprovePurchaseRequisitionCommandHandler(
    IRequisitionRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ApprovePurchaseRequisitionCommand, Result>
{
    public async Task<Result> HandleAsync(
        ApprovePurchaseRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var requisition = await repository.GetByIdAsync(request.RequisitionId, cancellationToken);

        if (requisition is null)
            return Result.Failure(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var result = requisition.Approve(request.ApproverId);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}