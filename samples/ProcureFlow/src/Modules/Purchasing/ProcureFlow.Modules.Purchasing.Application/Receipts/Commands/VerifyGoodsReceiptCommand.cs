using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed record VerifyGoodsReceiptCommand(
    Guid ReceiptId) : ICommand<Result>;