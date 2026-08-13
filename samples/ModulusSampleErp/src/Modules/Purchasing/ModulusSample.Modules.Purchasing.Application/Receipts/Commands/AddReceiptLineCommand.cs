using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed record AddReceiptLineCommand(
    Guid ReceiptId,
    Guid ProductId,
    decimal QuantityReceived,
    string LotNumber = "") : ICommand<Result>;