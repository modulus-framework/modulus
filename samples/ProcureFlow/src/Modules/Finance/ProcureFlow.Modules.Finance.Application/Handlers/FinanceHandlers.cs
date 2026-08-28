using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Finance.Application.Commands;
using ProcureFlow.Modules.Finance.Domain.Entities;
using ProcureFlow.Modules.Finance.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Finance.Application.Handlers;

public sealed class CreatePaymentProposalCommandHandler : ICommandHandler<CreatePaymentProposalCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IPaymentProposalRepository _proposalRepository;
    private readonly ILogger<CreatePaymentProposalCommandHandler> _logger;

    public CreatePaymentProposalCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IPaymentProposalRepository proposalRepository,
        ILogger<CreatePaymentProposalCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _proposalRepository = proposalRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreatePaymentProposalCommand request, CancellationToken ct)
    {
        var proposal = PaymentProposal.Create(
            tenantId: Guid.Empty,
            proposalNumber: request.ProposalNumber,
            paymentDate: request.PaymentDate,
            currency: request.Currency,
            totalAmount: request.TotalAmount,
            createdBy: "system"
        );

        foreach (var invoiceId in request.InvoiceIds)
        {
            proposal.AddInvoice(invoiceId);
        }

        _proposalRepository.Add(proposal);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Created payment proposal {ProposalNumber} with ID {ProposalId}", request.ProposalNumber, proposal.Id);
        return Result.Success(proposal.Id);
    }
}

public sealed class ApprovePaymentProposalCommandHandler : ICommandHandler<ApprovePaymentProposalCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IPaymentProposalRepository _proposalRepository;

    public ApprovePaymentProposalCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IPaymentProposalRepository proposalRepository)
    {
        _unitOfWork = unitOfWork;
        _proposalRepository = proposalRepository;
    }

    public async Task<Result> HandleAsync(ApprovePaymentProposalCommand request, CancellationToken ct)
    {
        var proposal = await _proposalRepository.GetByIdAsync(request.ProposalId, ct);
        if (proposal == null)
        {
            return Result.Failure(Error.NotFound("PaymentProposal.NotFound", $"Proposal with ID {request.ProposalId} not found"));
        }

        var result = proposal.Approve("system");
        if (!result.IsSuccess)
            return result;

        _proposalRepository.Update(proposal);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class ExportPaymentProposalCommandHandler : ICommandHandler<ExportPaymentProposalCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IPaymentProposalRepository _proposalRepository;

    public ExportPaymentProposalCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IPaymentProposalRepository proposalRepository)
    {
        _unitOfWork = unitOfWork;
        _proposalRepository = proposalRepository;
    }

    public async Task<Result> HandleAsync(ExportPaymentProposalCommand request, CancellationToken ct)
    {
        var proposal = await _proposalRepository.GetByIdAsync(request.ProposalId, ct);
        if (proposal == null)
        {
            return Result.Failure(Error.NotFound("PaymentProposal.NotFound", $"Proposal with ID {request.ProposalId} not found"));
        }

        var result = proposal.Export();
        if (!result.IsSuccess)
            return result;

        _proposalRepository.Update(proposal);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class CreateJournalBatchCommandHandler : ICommandHandler<CreateJournalBatchCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IJournalBatchRepository _journalRepository;
    private readonly ILogger<CreateJournalBatchCommandHandler> _logger;

    public CreateJournalBatchCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IJournalBatchRepository journalRepository,
        ILogger<CreateJournalBatchCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _journalRepository = journalRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateJournalBatchCommand request, CancellationToken ct)
    {
        var journal = JournalBatch.Create(
            tenantId: Guid.Empty,
            journalNumber: request.JournalNumber,
            postingDate: request.PostingDate,
            description: request.Description,
            currency: request.Currency,
            createdBy: "system"
        );

        foreach (var lineDto in request.Lines)
        {
            var line = new JournalLine(
                id: Guid.NewGuid(),
                accountCode: lineDto.AccountCode,
                accountName: lineDto.AccountName,
                description: lineDto.Description,
                debit: lineDto.Debit,
                credit: lineDto.Credit,
                costCenterId: lineDto.CostCenterId
            );
            journal.AddLine(line);
        }

        var validationResult = journal.Validate();
        if (!validationResult.IsSuccess)
            return Result.Failure<Guid>(validationResult.Error);

        _journalRepository.Add(journal);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Created journal batch {JournalNumber} with ID {JournalId}", request.JournalNumber, journal.Id);
        return Result.Success(journal.Id);
    }
}

public sealed class PostJournalBatchCommandHandler : ICommandHandler<PostJournalBatchCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IJournalBatchRepository _journalRepository;

    public PostJournalBatchCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IJournalBatchRepository journalRepository)
    {
        _unitOfWork = unitOfWork;
        _journalRepository = journalRepository;
    }

    public async Task<Result> HandleAsync(PostJournalBatchCommand request, CancellationToken ct)
    {
        var journal = await _journalRepository.GetByIdAsync(request.JournalId, ct);
        if (journal == null)
        {
            return Result.Failure(Error.NotFound("JournalBatch.NotFound", $"Journal batch with ID {request.JournalId} not found"));
        }

        var result = journal.Post("system");
        if (!result.IsSuccess)
            return result;

        _journalRepository.Update(journal);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class CreateFxRateCommandHandler : ICommandHandler<CreateFxRateCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IFxRateRepository _fxRateRepository;
    private readonly ILogger<CreateFxRateCommandHandler> _logger;

    public CreateFxRateCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IFxRateRepository fxRateRepository,
        ILogger<CreateFxRateCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _fxRateRepository = fxRateRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateFxRateCommand request, CancellationToken ct)
    {
        var fxRate = new FxRate(
            id: Guid.NewGuid(),
            tenantId: Guid.Empty,
            effectiveDate: request.EffectiveDate,
            fromCurrency: request.FromCurrency,
            toCurrency: request.ToCurrency,
            rate: request.Rate,
            source: request.Source,
            sourceReference: request.SourceReference,
            uploadedAtUtc: DateTime.UtcNow
        );

        _fxRateRepository.Add(fxRate);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Created FX rate {FromCurrency}->{ToCurrency} effective {EffectiveDate}", request.FromCurrency, request.ToCurrency, request.EffectiveDate);
        return Result.Success(fxRate.Id);
    }
}

public sealed class CreateCostCenterCommandHandler : ICommandHandler<CreateCostCenterCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly ICostCenterRepository _costCenterRepository;
    private readonly ILogger<CreateCostCenterCommandHandler> _logger;

    public CreateCostCenterCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        ICostCenterRepository costCenterRepository,
        ILogger<CreateCostCenterCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _costCenterRepository = costCenterRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateCostCenterCommand request, CancellationToken ct)
    {
        var costCenter = new CostCenter(
            id: Guid.NewGuid(),
            tenantId: Guid.Empty,
            code: request.Code,
            name: request.Name,
            parentId: request.ParentId,
            isActive: request.IsActive
        );

        _costCenterRepository.Add(costCenter);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Created cost center {Code} with ID {CostCenterId}", request.Code, costCenter.Id);
        return Result.Success(costCenter.Id);
    }
}

// ── Match Exception Command Handlers (BR-FIN-12) ────────────────────

public sealed class ApproveMatchExceptionCommandHandler : ICommandHandler<ApproveMatchExceptionCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IMatchExceptionRepository _repository;

    public ApproveMatchExceptionCommandHandler(IFinanceUnitOfWork unitOfWork, IMatchExceptionRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<Result> HandleAsync(ApproveMatchExceptionCommand request, CancellationToken ct)
    {
        var exception = await _repository.GetByIdAsync(request.ExceptionId, ct);
        if (exception is null)
            return Result.Failure(Error.NotFound("MatchException.NotFound", "Match exception not found"));

        var result = exception.Approve("system", request.Notes);
        if (!result.IsSuccess)
            return result;

        _repository.Update(exception);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class RejectMatchExceptionCommandHandler : ICommandHandler<RejectMatchExceptionCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IMatchExceptionRepository _repository;

    public RejectMatchExceptionCommandHandler(IFinanceUnitOfWork unitOfWork, IMatchExceptionRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<Result> HandleAsync(RejectMatchExceptionCommand request, CancellationToken ct)
    {
        var exception = await _repository.GetByIdAsync(request.ExceptionId, ct);
        if (exception is null)
            return Result.Failure(Error.NotFound("MatchException.NotFound", "Match exception not found"));

        var result = exception.Reject("system", request.Reason);
        if (!result.IsSuccess)
            return result;

        _repository.Update(exception);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class OverrideMatchExceptionCommandHandler : ICommandHandler<OverrideMatchExceptionCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IMatchExceptionRepository _repository;

    public OverrideMatchExceptionCommandHandler(IFinanceUnitOfWork unitOfWork, IMatchExceptionRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<Result> HandleAsync(OverrideMatchExceptionCommand request, CancellationToken ct)
    {
        var exception = await _repository.GetByIdAsync(request.ExceptionId, ct);
        if (exception is null)
            return Result.Failure(Error.NotFound("MatchException.NotFound", "Match exception not found"));

        var result = exception.Override("system", request.Reason);
        if (!result.IsSuccess)
            return result;

        _repository.Update(exception);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── GR/IR Accrual Command Handlers (BR-FIN-13) ──────────────────────

public sealed class CreateGrIrAccrualCommandHandler : ICommandHandler<CreateGrIrAccrualCommand, Result<Guid>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IGrIrAccrualRepository _repository;

    public CreateGrIrAccrualCommandHandler(IFinanceUnitOfWork unitOfWork, IGrIrAccrualRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<Result<Guid>> HandleAsync(CreateGrIrAccrualCommand request, CancellationToken ct)
    {
        var existing = await _repository.GetByGrnIdAsync(request.GrnId, ct);
        if (existing is not null)
            return Result.Failure<Guid>(Error.Conflict("GrIrAccrual.Duplicate", "Accrual already exists for this GRN"));

        var accrual = GrIrAccrual.Create(
            tenantId: Guid.Empty,
            grnId: request.GrnId,
            poId: request.PoId,
            vendorId: request.VendorId,
            grnNumber: request.GrnNumber,
            receivedOn: request.ReceivedOn,
            amount: request.Amount,
            currency: request.Currency,
            createdBy: "system");

        _repository.Add(accrual);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(accrual.Id);
    }
}

public sealed class ClearGrIrAccrualCommandHandler : ICommandHandler<ClearGrIrAccrualCommand, Result>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IGrIrAccrualRepository _repository;

    public ClearGrIrAccrualCommandHandler(IFinanceUnitOfWork unitOfWork, IGrIrAccrualRepository repository)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
    }

    public async Task<Result> HandleAsync(ClearGrIrAccrualCommand request, CancellationToken ct)
    {
        var accrual = await _repository.GetByIdAsync(request.AccrualId, ct);
        if (accrual is null)
            return Result.Failure(Error.NotFound("GrIrAccrual.NotFound", "GR/IR accrual not found"));

        var result = accrual.Clear(request.InvoiceId, request.ClearedOn, "system");
        if (!result.IsSuccess)
            return result;

        _repository.Update(accrual);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── 3-Way Match Command Handler (BR-FIN-12) ─────────────────────────

public sealed class PerformThreeWayMatchCommandHandler : ICommandHandler<PerformThreeWayMatchCommand, Result<ThreeWayMatchResult>>
{
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IApInvoiceRepository _invoiceRepository;
    private readonly IMatchExceptionRepository _exceptionRepository;
    private readonly ILogger<PerformThreeWayMatchCommandHandler> _logger;

    private const decimal DefaultQtyTolerancePercent = 2.0m;
    private const decimal DefaultPriceTolerancePercent = 2.0m;

    public PerformThreeWayMatchCommandHandler(
        IFinanceUnitOfWork unitOfWork,
        IApInvoiceRepository invoiceRepository,
        IMatchExceptionRepository exceptionRepository,
        ILogger<PerformThreeWayMatchCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _invoiceRepository = invoiceRepository;
        _exceptionRepository = exceptionRepository;
        _logger = logger;
    }

    public async Task<Result<ThreeWayMatchResult>> HandleAsync(PerformThreeWayMatchCommand request, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<ThreeWayMatchResult>(Error.NotFound("ApInvoice.NotFound", "Invoice not found"));

        if (invoice.Status != ApInvoiceStatus.Submitted)
            return Result.Failure<ThreeWayMatchResult>(Error.BusinessRule("ApInvoice.InvalidState", "Only submitted invoices can be matched"));

        var matchedCount = 0;
        var exceptionCount = 0;
        var exceptions = new List<ThreeWayMatchExceptionDto>();

        foreach (var lineInput in request.LineInputs)
        {
            var invoiceLine = invoice.Lines.FirstOrDefault(l => l.Id == lineInput.InvoiceLineId);
            if (invoiceLine is null)
                continue;

            var qtyVariance = CalculateVariancePercent(invoiceLine.Quantity, lineInput.GrnReceivedQty);
            var priceVariance = CalculateVariancePercent(invoiceLine.UnitPrice, lineInput.PoUnitPrice);

            var hasQtyException = qtyVariance > DefaultQtyTolerancePercent;
            var hasPriceException = priceVariance > DefaultPriceTolerancePercent;

            if (hasQtyException || hasPriceException)
            {
                if (hasQtyException)
                {
                    var qtyEx = MatchException.Create(
                        invoice.TenantId, invoice.Id, invoiceLine.Id,
                        MatchExceptionType.QtyVariance,
                        invoiceLine.Quantity, lineInput.GrnReceivedQty,
                        invoiceLine.UnitPrice, lineInput.PoUnitPrice,
                        $"Qty variance {qtyVariance:F1}% exceeds tolerance ({DefaultQtyTolerancePercent}%)");
                    _exceptionRepository.Add(qtyEx);
                    exceptions.Add(MapToDto(qtyEx));
                }

                if (hasPriceException)
                {
                    var priceEx = MatchException.Create(
                        invoice.TenantId, invoice.Id, invoiceLine.Id,
                        MatchExceptionType.PriceVariance,
                        invoiceLine.Quantity, lineInput.GrnReceivedQty,
                        invoiceLine.UnitPrice, lineInput.PoUnitPrice,
                        $"Price variance {priceVariance:F1}% exceeds tolerance ({DefaultPriceTolerancePercent}%)");
                    _exceptionRepository.Add(priceEx);
                    exceptions.Add(MapToDto(priceEx));
                }

                invoiceLine.MarkMatched(InvoiceLineMatchStatus.Exception, "Tolerance exceeded");
                exceptionCount++;
            }
            else
            {
                invoiceLine.MarkMatched(InvoiceLineMatchStatus.Matched);
                matchedCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("3-way match completed for invoice {InvoiceId}: {Matched}/{Total} matched, {Exceptions} exceptions",
            request.InvoiceId, matchedCount, invoice.Lines.Count, exceptionCount);

        return Result.Success(new ThreeWayMatchResult(
            invoice.Lines.Count,
            matchedCount,
            exceptionCount,
            exceptions));
    }

    private static decimal CalculateVariancePercent(decimal actual, decimal expected)
    {
        if (expected == 0) return actual == 0 ? 0 : 100;
        return Math.Abs(actual - expected) / expected * 100;
    }

    private static ThreeWayMatchExceptionDto MapToDto(MatchException ex) => new(
        ex.Id,
        ex.InvoiceLineId,
        ex.Type.ToString(),
        ex.InvoiceQty,
        ex.MatchedQty,
        ex.InvoicePrice,
        ex.MatchedPrice,
        ex.Description,
        ex.Status.ToString());
}