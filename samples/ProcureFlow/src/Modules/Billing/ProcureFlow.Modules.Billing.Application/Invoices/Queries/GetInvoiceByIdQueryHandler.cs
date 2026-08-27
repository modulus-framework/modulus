using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Application.Invoices.Dtos;
using ModulusSample.Modules.Billing.Domain.Repositories;

namespace ModulusSample.Modules.Billing.Application.Invoices.Queries;

public sealed class GetInvoiceByIdQueryHandler(
    IInvoiceRepository repository) : IQueryHandler<GetInvoiceByIdQuery, InvoiceDto?>
{
    public async Task<InvoiceDto?> HandleAsync(
        GetInvoiceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(request.InvoiceId, cancellationToken);

        if (invoice is null)
            return null;

        return new InvoiceDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.SalesOrderId,
            invoice.CustomerId,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.SubTotal,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.Status,
            invoice.Currency,
            invoice.Lines.Select(l => new InvoiceLineDto(
                l.Id, l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.TaxRate, l.LineTotal, l.TaxAmount)).ToList());
    }
}