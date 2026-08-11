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

internal sealed class RejectPurchaseRequisitionCommandHandler
    : ICommandHandler<RejectPurchaseRequisitionCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public RejectPurchaseRequisitionCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        RejectPurchaseRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var requisition = await _dbContext.Requisitions.FindAsync(
            new object[] { request.RequisitionId }, cancellationToken);

        if (requisition is null)
            return Result.Failure(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var result = requisition.Reject(request.Reason);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreatePurchaseOrderCommandHandler
    : ICommandHandler<CreatePurchaseOrderCommand, Result<Guid>>
{
    private readonly PurchasingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreatePurchaseOrderCommandHandler(
        PurchasingDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var requisition = await _dbContext.Requisitions.FindAsync(
            new object[] { request.RequisitionId }, cancellationToken);

        if (requisition is null)
            return Result.Failure<Guid>(Error.NotFound("Requisition.NotFound", "Requisition not found"));

        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
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

        _dbContext.Orders.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(orderId);
    }
}

internal sealed class SendPurchaseOrderCommandHandler
    : ICommandHandler<SendPurchaseOrderCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public SendPurchaseOrderCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        SendPurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.FindAsync(
            new object[] { request.OrderId }, cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order.NotFound", "Purchase order not found"));

        var result = order.Send();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreateGoodsReceiptCommandHandler
    : ICommandHandler<CreateGoodsReceiptCommand, Result<Guid>>
{
    private readonly PurchasingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateGoodsReceiptCommandHandler(
        PurchasingDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateGoodsReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.FindAsync(
            new object[] { request.PurchaseOrderId }, cancellationToken);

        if (order is null)
            return Result.Failure<Guid>(Error.NotFound("Order.NotFound", "Purchase order not found"));

        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
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

        _dbContext.Receipts.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(receiptId);
    }
}

internal sealed class AddReceiptLineCommandHandler
    : ICommandHandler<AddReceiptLineCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public AddReceiptLineCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        AddReceiptLineCommand request,
        CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.Receipts.FindAsync(
            new object[] { request.ReceiptId }, cancellationToken);

        if (receipt is null)
            return Result.Failure(Error.NotFound("Receipt.NotFound", "Goods receipt not found"));

        var result = receipt.AddLine(request.ProductId, request.QuantityReceived, request.LotNumber);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class VerifyGoodsReceiptCommandHandler
    : ICommandHandler<VerifyGoodsReceiptCommand, Result>
{
    private readonly PurchasingDbContext _dbContext;

    public VerifyGoodsReceiptCommandHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        VerifyGoodsReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.Receipts.FindAsync(
            new object[] { request.ReceiptId }, cancellationToken);

        if (receipt is null)
            return Result.Failure(Error.NotFound("Receipt.NotFound", "Goods receipt not found"));

        var result = receipt.Verify();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
