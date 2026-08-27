using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed record AddInvoiceLineCommand(
    Guid InvoiceId,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate = 0.1m) : ICommand<Result>;