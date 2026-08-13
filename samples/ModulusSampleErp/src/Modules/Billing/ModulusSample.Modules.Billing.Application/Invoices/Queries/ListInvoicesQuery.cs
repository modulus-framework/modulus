using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Queries;

public sealed record ListInvoicesQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<InvoiceDto>>;