using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Commands;

public sealed record CreatePurchaseRequisitionCommand(
    string RequisitionNumber,
    Guid OrgUnitId) : ICommand<Result<Guid>>;

public sealed record SubmitPurchaseRequisitionCommand(
    Guid RequisitionId) : ICommand<Result>;

public sealed record ApprovePurchaseRequisitionCommand(
    Guid RequisitionId,
    Guid ApproverId) : ICommand<Result>;

public sealed record RejectPurchaseRequisitionCommand(
    Guid RequisitionId,
    string Reason) : ICommand<Result>;

public sealed record AddRequisitionLineCommand(
    Guid RequisitionId,
    Guid SupplierId,
    string Description,
    decimal Quantity,
    decimal UnitPrice) : ICommand<Result>;

public sealed record CreatePurchaseOrderCommand(
    string OrderNumber,
    Guid RequisitionId,
    Guid SupplierId,
    Guid OrgUnitId) : ICommand<Result<Guid>>;

public sealed record SendPurchaseOrderCommand(
    Guid OrderId) : ICommand<Result>;

public sealed record CreateGoodsReceiptCommand(
    string ReceiptNumber,
    Guid PurchaseOrderId,
    Guid OrgUnitId) : ICommand<Result<Guid>>;

public sealed record AddReceiptLineCommand(
    Guid ReceiptId,
    Guid ProductId,
    decimal QuantityReceived,
    string LotNumber = "") : ICommand<Result>;

public sealed record VerifyGoodsReceiptCommand(
    Guid ReceiptId) : ICommand<Result>;
