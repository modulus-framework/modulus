using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Payments.Queries;

public sealed class ListPaymentsQueryHandler(
    IPaymentRepository repository) : IQueryHandler<ListPaymentsQuery, PagedResult<PaymentDto>>
{
    public async Task<PagedResult<PaymentDto>> HandleAsync(
        ListPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.PageNumber, request.PageSize, cancellationToken);

        var data = page.Items.Select(p => new PaymentDto(
            p.Id,
            p.PaymentNumber,
            p.InvoiceId,
            p.PaymentDate,
            p.Amount,
            p.PaymentMethod,
            p.Status,
            p.ReferenceNumber)).ToList();

        return new PagedResult<PaymentDto>(data, page.TotalCount, request.PageNumber, request.PageSize);
    }
}