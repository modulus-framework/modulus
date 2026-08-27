namespace ModulusSample.Modules.Billing.Application.Payments.Dtos;

public sealed record PaymentDto(
    Guid Id,
    string PaymentNumber,
    Guid InvoiceId,
    DateTime PaymentDate,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ReferenceNumber);