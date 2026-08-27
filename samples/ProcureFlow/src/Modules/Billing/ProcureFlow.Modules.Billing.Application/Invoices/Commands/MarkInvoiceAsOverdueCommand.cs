using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed record MarkInvoiceAsOverdueCommand(
    Guid InvoiceId) : ICommand<Result>;