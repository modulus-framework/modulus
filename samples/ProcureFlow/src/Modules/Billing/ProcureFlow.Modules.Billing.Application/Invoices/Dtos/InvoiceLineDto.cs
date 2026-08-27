namespace ModulusSample.Modules.Billing.Application.Invoices.Dtos;

public sealed record InvoiceLineDto(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineTotal,
    decimal TaxAmount);