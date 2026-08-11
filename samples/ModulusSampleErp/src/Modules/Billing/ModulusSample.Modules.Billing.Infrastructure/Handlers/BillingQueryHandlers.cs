using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Modules.Billing.Infrastructure.Database;

namespace ModulusSample.Modules.Billing.Infrastructure.Handlers;

internal sealed class GetInvoiceByIdQueryHandler : IQueryHandler<GetInvoiceByIdQuery, InvoiceDto?>
{
    private readonly BillingDbContext _dbContext;

    public GetInvoiceByIdQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InvoiceDto?> HandleAsync(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);

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

internal sealed class ListInvoicesQueryHandler : IQueryHandler<ListInvoicesQuery, PagedResult<InvoiceDto>>
{
    private readonly BillingDbContext _dbContext;

    public ListInvoicesQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<InvoiceDto>> HandleAsync(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Invoices.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);

        var invoices = await query
            .OrderByDescending(i => i.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var data = invoices.Select(i => new InvoiceDto(
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
                l.Id, l.ProductId, l.Description, l.Quantity, l.UnitPrice, l.TaxRate, l.LineTotal, l.TaxAmount)).ToList())).ToList();

        return new PagedResult<InvoiceDto>(data, totalCount, request.PageNumber, request.PageSize);
    }
}

internal sealed class GetPaymentByIdQueryHandler : IQueryHandler<GetPaymentByIdQuery, PaymentDto?>
{
    private readonly BillingDbContext _dbContext;

    public GetPaymentByIdQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaymentDto?> HandleAsync(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
            return null;

        return new PaymentDto(
            payment.Id,
            payment.PaymentNumber,
            payment.InvoiceId,
            payment.PaymentDate,
            payment.Amount,
            payment.PaymentMethod,
            payment.Status,
            payment.ReferenceNumber);
    }
}

internal sealed class ListPaymentsQueryHandler : IQueryHandler<ListPaymentsQuery, PagedResult<PaymentDto>>
{
    private readonly BillingDbContext _dbContext;

    public ListPaymentsQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<PaymentDto>> HandleAsync(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Payments.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);

        var payments = await query
            .OrderByDescending(p => p.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var data = payments.Select(p => new PaymentDto(
            p.Id,
            p.PaymentNumber,
            p.InvoiceId,
            p.PaymentDate,
            p.Amount,
            p.PaymentMethod,
            p.Status,
            p.ReferenceNumber)).ToList();

        return new PagedResult<PaymentDto>(data, totalCount, request.PageNumber, request.PageSize);
    }
}

internal sealed class GetCreditNoteByIdQueryHandler : IQueryHandler<GetCreditNoteByIdQuery, CreditNoteDto?>
{
    private readonly BillingDbContext _dbContext;

    public GetCreditNoteByIdQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreditNoteDto?> HandleAsync(GetCreditNoteByIdQuery request, CancellationToken cancellationToken)
    {
        var creditNote = await _dbContext.CreditNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(cn => cn.Id == request.CreditNoteId, cancellationToken);

        if (creditNote is null)
            return null;

        return new CreditNoteDto(
            creditNote.Id,
            creditNote.CreditNoteNumber,
            creditNote.InvoiceId,
            creditNote.IssuedDate,
            creditNote.Amount,
            creditNote.Reason,
            creditNote.Status);
    }
}

internal sealed class ListCreditNotesQueryHandler : IQueryHandler<ListCreditNotesQuery, PagedResult<CreditNoteDto>>
{
    private readonly BillingDbContext _dbContext;

    public ListCreditNotesQueryHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<CreditNoteDto>> HandleAsync(ListCreditNotesQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.CreditNotes.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);

        var creditNotes = await query
            .OrderByDescending(cn => cn.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var data = creditNotes.Select(cn => new CreditNoteDto(
            cn.Id,
            cn.CreditNoteNumber,
            cn.InvoiceId,
            cn.IssuedDate,
            cn.Amount,
            cn.Reason,
            cn.Status)).ToList();

        return new PagedResult<CreditNoteDto>(data, totalCount, request.PageNumber, request.PageSize);
    }
}
