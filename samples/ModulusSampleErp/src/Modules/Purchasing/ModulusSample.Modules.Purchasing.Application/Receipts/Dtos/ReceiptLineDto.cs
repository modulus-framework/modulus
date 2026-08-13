namespace ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;

public sealed record ReceiptLineDto(
    Guid Id,
    Guid ProductId,
    decimal QuantityReceived,
    string LotNumber);