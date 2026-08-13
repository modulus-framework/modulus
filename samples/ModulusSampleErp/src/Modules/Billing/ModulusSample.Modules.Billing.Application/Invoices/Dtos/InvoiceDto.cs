namespace ModulusSample.Modules.Billing.Application.Invoices.Dtos;

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