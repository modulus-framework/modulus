using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed record SendInvoiceCommand(
    Guid InvoiceId) : ICommand<Result>;