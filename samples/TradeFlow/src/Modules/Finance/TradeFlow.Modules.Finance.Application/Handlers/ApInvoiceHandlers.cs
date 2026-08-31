using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Microsoft.Extensions.Logging;
using TradeFlow.Modules.Finance.Application.Commands;
using TradeFlow.Modules.Finance.Application.Dtos;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Finance.Application.Handlers;

public sealed class CreateApInvoiceCommandHandler : ICommandHandler<CreateApInvoiceCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IApInvoiceRepository _invoiceRepository;
    private readonly ILogger<CreateApInvoiceCommandHandler> _logger;

    public CreateApInvoiceCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IApInvoiceRepository invoiceRepository,
        ILogger<CreateApInvoiceCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _invoiceRepository = invoiceRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateApInvoiceCommand request, CancellationToken ct)
    {
        var existing = await _invoiceRepository.ExistsByNumberAsync(request.InvoiceNumber, ct: ct);
        if (existing)
        {
            return Result.Failure<Guid>(Error.Validation("ApInvoice.DuplicateNumber",
                $"Invoice number '{request.InvoiceNumber}' already exists"));
        }

        var invoice = ApInvoice.Create(
            tenantId: Guid.Empty,
            invoiceNumber: request.InvoiceNumber,
            vendorId: request.VendorId,
            invoiceDate: request.InvoiceDate,
            dueDate: request.DueDate,
            currency: request.Currency,
            totalAmount: request.TotalAmount,
            source: request.Source,
            isCreditNote: request.IsCreditNote,
            createdBy: "system"
        );

        foreach (var lineDto in request.Lines)
        {
            var line = new InvoiceLine(
                id: Guid.NewGuid(),
                poLineId: lineDto.PoLineId,
                grnLineId: lineDto.GrnLineId,
                description: lineDto.Description,
                quantity: lineDto.Quantity,
                uom: lineDto.Uom,
                unitPrice: lineDto.UnitPrice,
                lineTotal: lineDto.LineTotal
            );
            invoice.AddLine(line);
        }

        _invoiceRepository.Add(invoice);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Created AP invoice {InvoiceNumber} with ID {InvoiceId}", request.InvoiceNumber, invoice.Id);
        return Result.Success(invoice.Id);
    }
}

public sealed class SubmitApInvoiceCommandHandler : ICommandHandler<SubmitApInvoiceCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IApInvoiceRepository _invoiceRepository;

    public SubmitApInvoiceCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IApInvoiceRepository invoiceRepository)
    {
        _unitOfWork = unitOfWork;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result> HandleAsync(SubmitApInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, ct);
        if (invoice == null)
        {
            return Result.Failure(Error.NotFound("ApInvoice.NotFound", $"Invoice with ID {request.InvoiceId} not found"));
        }

        var result = invoice.Submit();
        if (!result.IsSuccess)
            return result;

        _invoiceRepository.Update(invoice);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class ApproveApInvoiceCommandHandler : ICommandHandler<ApproveApInvoiceCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IApInvoiceRepository _invoiceRepository;

    public ApproveApInvoiceCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IApInvoiceRepository invoiceRepository)
    {
        _unitOfWork = unitOfWork;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result> HandleAsync(ApproveApInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, ct);
        if (invoice == null)
        {
            return Result.Failure(Error.NotFound("ApInvoice.NotFound", $"Invoice with ID {request.InvoiceId} not found"));
        }

        var result = invoice.Approve("system");
        if (!result.IsSuccess)
            return result;

        _invoiceRepository.Update(invoice);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class CancelApInvoiceCommandHandler : ICommandHandler<CancelApInvoiceCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IApInvoiceRepository _invoiceRepository;

    public CancelApInvoiceCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IApInvoiceRepository invoiceRepository)
    {
        _unitOfWork = unitOfWork;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result> HandleAsync(CancelApInvoiceCommand request, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, ct);
        if (invoice == null)
        {
            return Result.Failure(Error.NotFound("ApInvoice.NotFound", $"Invoice with ID {request.InvoiceId} not found"));
        }

        var result = invoice.Cancel(request.Reason, "system");
        if (!result.IsSuccess)
            return result;

        _invoiceRepository.Update(invoice);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}