using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Infrastructure.Handlers;

internal sealed class CreateInvoiceCommandHandler
    : ICommandHandler<CreateInvoiceCommand, Result<Guid>>
{
    private readonly BillingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateInvoiceCommandHandler(
        BillingDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var invoiceId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = Invoice.Create(
            invoiceId,
            request.InvoiceNumber,
            request.SalesOrderId,
            request.CustomerId,
            orgUnitId,
            tenantId,
            request.Currency);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.Invoices.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(invoiceId);
    }
}

internal sealed class AddInvoiceLineCommandHandler
    : ICommandHandler<AddInvoiceLineCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public AddInvoiceLineCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        AddInvoiceLineCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices.FindAsync(
            new object[] { request.InvoiceId }, cancellationToken);

        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Invoice not found"));

        var result = invoice.AddLine(request.ProductId, request.Description, request.Quantity, request.UnitPrice, request.TaxRate);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SendInvoiceCommandHandler
    : ICommandHandler<SendInvoiceCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public SendInvoiceCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        SendInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices.FindAsync(
            new object[] { request.InvoiceId }, cancellationToken);

        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Invoice not found"));

        var result = invoice.Send();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class MarkInvoiceAsPaidCommandHandler
    : ICommandHandler<MarkInvoiceAsPaidCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public MarkInvoiceAsPaidCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        MarkInvoiceAsPaidCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices.FindAsync(
            new object[] { request.InvoiceId }, cancellationToken);

        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Invoice not found"));

        var result = invoice.MarkAsPaid();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class MarkInvoiceAsOverdueCommandHandler
    : ICommandHandler<MarkInvoiceAsOverdueCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public MarkInvoiceAsOverdueCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        MarkInvoiceAsOverdueCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.Invoices.FindAsync(
            new object[] { request.InvoiceId }, cancellationToken);

        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Invoice not found"));

        var result = invoice.MarkAsOverdue();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreatePaymentCommandHandler
    : ICommandHandler<CreatePaymentCommand, Result<Guid>>
{
    private readonly BillingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreatePaymentCommandHandler(
        BillingDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreatePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var paymentId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = Payment.Create(
            paymentId,
            request.PaymentNumber,
            request.InvoiceId,
            request.Amount,
            request.PaymentMethod,
            orgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.Payments.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(paymentId);
    }
}

internal sealed class ConfirmPaymentCommandHandler
    : ICommandHandler<ConfirmPaymentCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public ConfirmPaymentCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        ConfirmPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments.FindAsync(
            new object[] { request.PaymentId }, cancellationToken);

        if (payment is null)
            return Result.Failure(Error.NotFound("Payment.NotFound", "Payment not found"));

        var result = payment.Confirm(request.ReferenceNumber);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreateCreditNoteCommandHandler
    : ICommandHandler<CreateCreditNoteCommand, Result<Guid>>
{
    private readonly BillingDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateCreditNoteCommandHandler(
        BillingDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateCreditNoteCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var creditNoteId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = CreditNote.Create(
            creditNoteId,
            request.CreditNoteNumber,
            request.InvoiceId,
            request.Amount,
            request.Reason,
            orgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.CreditNotes.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(creditNoteId);
    }
}

internal sealed class IssueCreditNoteCommandHandler
    : ICommandHandler<IssueCreditNoteCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public IssueCreditNoteCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        IssueCreditNoteCommand request,
        CancellationToken cancellationToken)
    {
        var creditNote = await _dbContext.CreditNotes.FindAsync(
            new object[] { request.CreditNoteId }, cancellationToken);

        if (creditNote is null)
            return Result.Failure(Error.NotFound("CreditNote.NotFound", "Credit note not found"));

        var result = creditNote.Issue();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ApplyCreditNoteCommandHandler
    : ICommandHandler<ApplyCreditNoteCommand, Result>
{
    private readonly BillingDbContext _dbContext;

    public ApplyCreditNoteCommandHandler(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> HandleAsync(
        ApplyCreditNoteCommand request,
        CancellationToken cancellationToken)
    {
        var creditNote = await _dbContext.CreditNotes.FindAsync(
            new object[] { request.CreditNoteId }, cancellationToken);

        if (creditNote is null)
            return Result.Failure(Error.NotFound("CreditNote.NotFound", "Credit note not found"));

        var result = creditNote.Apply();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
