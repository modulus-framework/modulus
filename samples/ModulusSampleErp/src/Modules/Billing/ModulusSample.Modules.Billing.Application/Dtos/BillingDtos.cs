namespace ModulusSample.Modules.Billing.Application.Dtos;

public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    Guid SalesOrderId,
    Guid CustomerId,
    DateTime InvoiceDate,
    DateTime DueDate,
    decimal SubTotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string Status,
    string Currency,
    List<InvoiceLineDto> Lines);

public sealed record InvoiceLineDto(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineTotal,
    decimal TaxAmount);

public sealed record PaymentDto(
    Guid Id,
    string PaymentNumber,
    Guid InvoiceId,
    DateTime PaymentDate,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ReferenceNumber);

public sealed record CreditNoteDto(
    Guid Id,
    string CreditNoteNumber,
    Guid InvoiceId,
    DateTime IssuedDate,
    decimal Amount,
    string Reason,
    string Status);
