using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed record CreateGoodsReceiptCommand(
    string ReceiptNumber,
    Guid PurchaseOrderId,
    Guid OrgUnitId) : ICommand<Result<Guid>>;