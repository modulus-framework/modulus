using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed record CreateInvoiceCommand(
    string InvoiceNumber,
    Guid SalesOrderId,
    Guid CustomerId,
    string Currency = "USD") : ICommand<Result<Guid>>;