using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Queries;

public sealed class ListInvoicesQueryHandler(
    IInvoiceRepository repository) : IQueryHandler<ListInvoicesQuery, PagedResult<InvoiceDto>>
{
    public async Task<PagedResult<InvoiceDto>> HandleAsync(
        ListInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(request.PageNumber, request.PageSize, cancellationToken);

        var data = page.Items.Select(i => new InvoiceDto(
            i.Id,
            i.InvoiceNumber,
            i.SalesOrderId,
            i.CustomerId,
            i.InvoiceDate,
            i.DueDate,
            i.SubTotal,
            i.TaxAmount,
            i.TotalAmount,
            i.Status,
            i.Currency,
            i.Lines.Select(l => new InvoiceLineDto(
                l.Id, l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.TaxRate, l.LineTotal, l.TaxAmount)).ToList()))
            .ToList();

        return new PagedResult<InvoiceDto>(data, page.TotalCount, request.PageNumber, request.PageSize);
    }
}