using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;

namespace ModulusSample.Modules.Billing.Application.Invoices.Queries;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId) : IQuery<InvoiceDto?>;