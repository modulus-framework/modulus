using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;

namespace ModulusSample.Modules.Billing.Application.Queries;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId) : IQuery<InvoiceDto?>;

public sealed record ListInvoicesQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<InvoiceDto>>;

public sealed record GetPaymentByIdQuery(Guid PaymentId) : IQuery<PaymentDto?>;

public sealed record ListPaymentsQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<PaymentDto>>;

public sealed record GetCreditNoteByIdQuery(Guid CreditNoteId) : IQuery<CreditNoteDto?>;

public sealed record ListCreditNotesQuery(
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedResult<CreditNoteDto>>;
