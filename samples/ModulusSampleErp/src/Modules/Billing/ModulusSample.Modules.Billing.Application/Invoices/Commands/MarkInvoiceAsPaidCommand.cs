using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed record MarkInvoiceAsPaidCommand(
    Guid InvoiceId) : ICommand<Result>;