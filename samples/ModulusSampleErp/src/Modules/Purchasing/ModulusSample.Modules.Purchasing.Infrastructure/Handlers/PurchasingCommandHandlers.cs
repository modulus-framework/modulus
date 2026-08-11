using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Modules.Purchasing.Domain.Entities;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Handlers;

internal sealed class CreatePurchaseRequisitionCommandHandler
    : ICommandHandler<CreatePurchaseRequisitionCommand, Result<Guid>>
{
    private readonly PurchasingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CreatePurchaseRequisitionCommandHandler(
        PurchasingDbContext dbContext,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreatePurchaseRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var userId = _currentUser.UserId ?? Guid.Empty;
        var requisitionId = Guid.NewGuid();

        var result = PurchaseRequisition.Create(
            requisitionId,
            request.RequisitionNumber,
            userId,
            request.OrgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.Requisitions.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(requisitionId);
    }
}

internal sealed class SubmitPurchaseRequisitionCommandHandler : ICommandHandler<SubmitPurchaseRequisitionCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public SubmitPurchaseRequisitionCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(SubmitPurchaseRequisitionCommand request, CancellationToken cancellationToken)
    {
        var requisition = await _dbContext.Requisitions.FindAsync(
            new object[] { request.RequisitionId }, cancellationToken);

        if (requisition is null)
            return Result.Failure(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var result = requisition.Submit();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ApprovePurchaseRequisitionCommandHandler
    : ICommandHandler<ApprovePurchaseRequisitionCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public ApprovePurchaseRequisitionCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        ApprovePurchaseRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var requisition = await _dbContext.Requisitions.FindAsync(
            new object[] { request.RequisitionId }, cancellationToken);

        if (requisition is null)
            return Result.Failure(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var result = requisition.Approve(request.ApproverId);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
