using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Finance.Application.Dtos;
using TradeFlow.Modules.Finance.Application.Queries;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;

namespace TradeFlow.Modules.Finance.Application.Handlers;

public sealed class GetApInvoiceByIdQueryHandler : IQueryHandler<GetApInvoiceByIdQuery, ApInvoiceDto?>
{
    private readonly IApInvoiceRepository _invoiceRepository;

    public GetApInvoiceByIdQueryHandler(IApInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<ApInvoiceDto?> HandleAsync(GetApInvoiceByIdQuery request, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, ct);
        if (invoice == null)
            return null;

        return new ApInvoiceDto(
            invoice.Id,
            invoice.TenantId,
            invoice.InvoiceNumber,
            invoice.VendorId,
            invoice.InvoiceDate,
            invoice.ReceivedDate,
            invoice.DueDate,
            invoice.Currency,
            invoice.TotalAmount,
            invoice.Source,
            invoice.IsCreditNote,
            invoice.Status,
            invoice.CancelReason,
            invoice.PaidAmount,
            invoice.OutstandingAmount,
            invoice.CreatedBy,
            invoice.CreatedAtUtc,
            invoice.ApprovedBy,
            invoice.ApprovedAtUtc,
            invoice.Lines.Select(l => new InvoiceLineDto(
                l.Id,
                l.PoLineId,
                l.GrnLineId,
                l.Description,
                l.Quantity,
                l.Uom,
                l.UnitPrice,
                l.LineTotal,
                l.MatchStatus,
                l.MatchReason
            )).ToList()
        );
    }
}

public sealed class GetApInvoicesByVendorQueryHandler : IQueryHandler<GetApInvoicesByVendorQuery, IReadOnlyList<ApInvoiceDto>>
{
    private readonly IApInvoiceRepository _invoiceRepository;

    public GetApInvoicesByVendorQueryHandler(IApInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<IReadOnlyList<ApInvoiceDto>> HandleAsync(GetApInvoicesByVendorQuery request, CancellationToken ct)
    {
        var invoices = await _invoiceRepository.GetByVendorIdAsync(request.VendorId, ct);
        return invoices.Select(invoice => new ApInvoiceDto(
            invoice.Id,
            invoice.TenantId,
            invoice.InvoiceNumber,
            invoice.VendorId,
            invoice.InvoiceDate,
            invoice.ReceivedDate,
            invoice.DueDate,
            invoice.Currency,
            invoice.TotalAmount,
            invoice.Source,
            invoice.IsCreditNote,
            invoice.Status,
            invoice.CancelReason,
            invoice.PaidAmount,
            invoice.OutstandingAmount,
            invoice.CreatedBy,
            invoice.CreatedAtUtc,
            invoice.ApprovedBy,
            invoice.ApprovedAtUtc,
            invoice.Lines.Select(l => new InvoiceLineDto(
                l.Id,
                l.PoLineId,
                l.GrnLineId,
                l.Description,
                l.Quantity,
                l.Uom,
                l.UnitPrice,
                l.LineTotal,
                l.MatchStatus,
                l.MatchReason
            )).ToList()
        )).ToList();
    }
}

public sealed class GetOverdueInvoicesQueryHandler : IQueryHandler<GetOverdueInvoicesQuery, IReadOnlyList<ApInvoiceDto>>
{
    private readonly IApInvoiceRepository _invoiceRepository;

    public GetOverdueInvoicesQueryHandler(IApInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<IReadOnlyList<ApInvoiceDto>> HandleAsync(GetOverdueInvoicesQuery request, CancellationToken ct)
    {
        var invoices = await _invoiceRepository.GetOverdueInvoicesAsync(request.AsOfDate, ct);
        return invoices.Select(invoice => new ApInvoiceDto(
            invoice.Id,
            invoice.TenantId,
            invoice.InvoiceNumber,
            invoice.VendorId,
            invoice.InvoiceDate,
            invoice.ReceivedDate,
            invoice.DueDate,
            invoice.Currency,
            invoice.TotalAmount,
            invoice.Source,
            invoice.IsCreditNote,
            invoice.Status,
            invoice.CancelReason,
            invoice.PaidAmount,
            invoice.OutstandingAmount,
            invoice.CreatedBy,
            invoice.CreatedAtUtc,
            invoice.ApprovedBy,
            invoice.ApprovedAtUtc,
            invoice.Lines.Select(l => new InvoiceLineDto(
                l.Id,
                l.PoLineId,
                l.GrnLineId,
                l.Description,
                l.Quantity,
                l.Uom,
                l.UnitPrice,
                l.LineTotal,
                l.MatchStatus,
                l.MatchReason
            )).ToList()
        )).ToList();
    }
}

public sealed class GetFxRateQueryHandler : IQueryHandler<GetFxRateQuery, FxRateDto?>
{
    private readonly IFxRateRepository _fxRateRepository;

    public GetFxRateQueryHandler(IFxRateRepository fxRateRepository)
    {
        _fxRateRepository = fxRateRepository;
    }

    public async Task<FxRateDto?> HandleAsync(GetFxRateQuery request, CancellationToken ct)
    {
        var fxRate = await _fxRateRepository.GetEffectiveRateAsync(Guid.Empty, request.FromCurrency, request.ToCurrency, request.AsOfDate, ct);
        if (fxRate == null)
            return null;

        return new FxRateDto(
            fxRate.Id,
            fxRate.TenantId,
            fxRate.EffectiveDate,
            fxRate.FromCurrency,
            fxRate.ToCurrency,
            fxRate.Rate,
            fxRate.Source,
            fxRate.SourceReference,
            fxRate.UploadedAtUtc
        );
    }
}

public sealed class GetFxRatesQueryHandler : IQueryHandler<GetFxRatesQuery, IReadOnlyList<FxRateDto>>
{
    private readonly IFxRateRepository _fxRateRepository;

    public GetFxRatesQueryHandler(IFxRateRepository fxRateRepository)
    {
        _fxRateRepository = fxRateRepository;
    }

    public async Task<IReadOnlyList<FxRateDto>> HandleAsync(GetFxRatesQuery request, CancellationToken ct)
    {
        var fxRates = await _fxRateRepository.GetByDateRangeAsync(request.FromDate, request.ToDate, ct);
        return fxRates.Select(fxRate => new FxRateDto(
            fxRate.Id,
            fxRate.TenantId,
            fxRate.EffectiveDate,
            fxRate.FromCurrency,
            fxRate.ToCurrency,
            fxRate.Rate,
            fxRate.Source,
            fxRate.SourceReference,
            fxRate.UploadedAtUtc
        )).ToList();
    }
}

public sealed class GetCostCentersQueryHandler : IQueryHandler<GetCostCentersQuery, IReadOnlyList<CostCenterDto>>
{
    private readonly ICostCenterRepository _costCenterRepository;

    public GetCostCentersQueryHandler(ICostCenterRepository costCenterRepository)
    {
        _costCenterRepository = costCenterRepository;
    }

    public async Task<IReadOnlyList<CostCenterDto>> HandleAsync(GetCostCentersQuery request, CancellationToken ct)
    {
        var costCenters = await _costCenterRepository.GetActiveAsync(Guid.Empty, ct);
        return costCenters.Select(cc => new CostCenterDto(
            cc.Id,
            cc.TenantId,
            cc.Code,
            cc.Name,
            cc.ParentId,
            cc.IsActive
        )).ToList();
    }
}

public sealed class GetApInvoicesByStatusQueryHandler : IQueryHandler<GetApInvoicesByStatusQuery, IReadOnlyList<ApInvoiceDto>>
{
    private readonly IApInvoiceRepository _invoiceRepository;

    public GetApInvoicesByStatusQueryHandler(IApInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<IReadOnlyList<ApInvoiceDto>> HandleAsync(GetApInvoicesByStatusQuery request, CancellationToken ct)
    {
        var invoices = await _invoiceRepository.GetByStatusAsync(request.Status, ct);
        return invoices.Select(invoice => new ApInvoiceDto(
            invoice.Id,
            invoice.TenantId,
            invoice.InvoiceNumber,
            invoice.VendorId,
            invoice.InvoiceDate,
            invoice.ReceivedDate,
            invoice.DueDate,
            invoice.Currency,
            invoice.TotalAmount,
            invoice.Source,
            invoice.IsCreditNote,
            invoice.Status,
            invoice.CancelReason,
            invoice.PaidAmount,
            invoice.OutstandingAmount,
            invoice.CreatedBy,
            invoice.CreatedAtUtc,
            invoice.ApprovedBy,
            invoice.ApprovedAtUtc,
            invoice.Lines.Select(l => new InvoiceLineDto(
                l.Id,
                l.PoLineId,
                l.GrnLineId,
                l.Description,
                l.Quantity,
                l.Uom,
                l.UnitPrice,
                l.LineTotal,
                l.MatchStatus,
                l.MatchReason
            )).ToList()
        )).ToList();
    }
}

public sealed class GetPaymentProposalByIdQueryHandler : IQueryHandler<GetPaymentProposalByIdQuery, PaymentProposalDto?>
{
    private readonly IPaymentProposalRepository _proposalRepository;

    public GetPaymentProposalByIdQueryHandler(IPaymentProposalRepository proposalRepository)
    {
        _proposalRepository = proposalRepository;
    }

    public async Task<PaymentProposalDto?> HandleAsync(GetPaymentProposalByIdQuery request, CancellationToken ct)
    {
        var proposal = await _proposalRepository.GetByIdAsync(request.ProposalId, ct);
        if (proposal == null)
            return null;

        return new PaymentProposalDto(
            proposal.Id,
            proposal.TenantId,
            proposal.ProposalNumber,
            proposal.PaymentDate,
            proposal.Currency,
            proposal.TotalAmount,
            proposal.Status,
            proposal.CreatedBy,
            proposal.CreatedAtUtc,
            proposal.ApprovedBy,
            proposal.ApprovedAtUtc,
            proposal.InvoiceIds.ToList()
        );
    }
}

public sealed class GetJournalBatchByIdQueryHandler : IQueryHandler<GetJournalBatchByIdQuery, JournalBatchDto?>
{
    private readonly IJournalBatchRepository _journalRepository;

    public GetJournalBatchByIdQueryHandler(IJournalBatchRepository journalRepository)
    {
        _journalRepository = journalRepository;
    }

    public async Task<JournalBatchDto?> HandleAsync(GetJournalBatchByIdQuery request, CancellationToken ct)
    {
        var journal = await _journalRepository.GetByIdAsync(request.JournalId, ct);
        if (journal == null)
            return null;

        return new JournalBatchDto(
            journal.Id,
            journal.TenantId,
            journal.JournalNumber,
            journal.PostingDate,
            journal.Description,
            journal.Currency,
            journal.Status,
            journal.TotalDebit,
            journal.TotalCredit,
            journal.CreatedBy,
            journal.CreatedAtUtc,
            journal.PostedBy,
            journal.PostedAtUtc,
            journal.Lines.Select(l => new JournalLineDto(
                l.Id,
                l.AccountCode,
                l.AccountName,
                l.Description,
                l.Debit,
                l.Credit,
                l.CostCenterId
            )).ToList()
        );
    }
}

// ── Match Exception Query Handlers (BR-FIN-12) ──────────────────────

public sealed class GetMatchExceptionByIdQueryHandler : IQueryHandler<GetMatchExceptionByIdQuery, MatchExceptionDto?>
{
    private readonly IMatchExceptionRepository _repository;

    public GetMatchExceptionByIdQueryHandler(IMatchExceptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<MatchExceptionDto?> HandleAsync(GetMatchExceptionByIdQuery request, CancellationToken ct)
    {
        var exception = await _repository.GetByIdAsync(request.ExceptionId, ct);
        if (exception == null)
            return null;

        return MatchExceptionMapper.ToDto(exception);
    }
}

public sealed class GetMatchExceptionsByInvoiceQueryHandler : IQueryHandler<GetMatchExceptionsByInvoiceQuery, IReadOnlyList<MatchExceptionDto>>
{
    private readonly IMatchExceptionRepository _repository;

    public GetMatchExceptionsByInvoiceQueryHandler(IMatchExceptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MatchExceptionDto>> HandleAsync(GetMatchExceptionsByInvoiceQuery request, CancellationToken ct)
    {
        var exceptions = await _repository.GetByInvoiceIdAsync(request.InvoiceId, ct);
        return exceptions.Select(MatchExceptionMapper.ToDto).ToList();
    }
}

public sealed class GetOpenMatchExceptionsQueryHandler : IQueryHandler<GetOpenMatchExceptionsQuery, IReadOnlyList<MatchExceptionDto>>
{
    private readonly IMatchExceptionRepository _repository;

    public GetOpenMatchExceptionsQueryHandler(IMatchExceptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MatchExceptionDto>> HandleAsync(GetOpenMatchExceptionsQuery request, CancellationToken ct)
    {
        var exceptions = await _repository.GetOpenAsync(ct);
        return exceptions.Select(MatchExceptionMapper.ToDto).ToList();
    }
}

internal static class MatchExceptionMapper
{
    internal static MatchExceptionDto ToDto(MatchException e) => new(
        e.Id, e.InvoiceId, e.InvoiceLineId, e.Type,
        e.InvoiceQty, e.MatchedQty, e.InvoicePrice, e.MatchedPrice,
        e.Description, e.Status, e.Resolution, e.ResolvedBy,
        e.ResolvedAtUtc, e.CreatedAtUtc);
}

// ── GR/IR Accrual Query Handlers (BR-FIN-13) ────────────────────────

public sealed class GetGrIrAccrualByIdQueryHandler : IQueryHandler<GetGrIrAccrualByIdQuery, GrIrAccrualDto?>
{
    private readonly IGrIrAccrualRepository _repository;

    public GetGrIrAccrualByIdQueryHandler(IGrIrAccrualRepository repository) => _repository = repository;

    public async Task<GrIrAccrualDto?> HandleAsync(GetGrIrAccrualByIdQuery request, CancellationToken ct)
    {
        var accrual = await _repository.GetByIdAsync(request.AccrualId, ct);
        return accrual is null ? null : GrIrAccrualMapper.ToDto(accrual);
    }
}

public sealed class GetOpenGrIrAccrualsQueryHandler : IQueryHandler<GetOpenGrIrAccrualsQuery, IReadOnlyList<GrIrAccrualDto>>
{
    private readonly IGrIrAccrualRepository _repository;

    public GetOpenGrIrAccrualsQueryHandler(IGrIrAccrualRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<GrIrAccrualDto>> HandleAsync(GetOpenGrIrAccrualsQuery request, CancellationToken ct)
    {
        var accruals = await _repository.GetOpenAsync(ct);
        return accruals.Select(GrIrAccrualMapper.ToDto).ToList();
    }
}

public sealed class GetGrIrAccrualsByVendorQueryHandler : IQueryHandler<GetGrIrAccrualsByVendorQuery, IReadOnlyList<GrIrAccrualDto>>
{
    private readonly IGrIrAccrualRepository _repository;

    public GetGrIrAccrualsByVendorQueryHandler(IGrIrAccrualRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<GrIrAccrualDto>> HandleAsync(GetGrIrAccrualsByVendorQuery request, CancellationToken ct)
    {
        var accruals = await _repository.GetByVendorIdAsync(request.VendorId, ct);
        return accruals.Select(GrIrAccrualMapper.ToDto).ToList();
    }
}

internal static class GrIrAccrualMapper
{
    internal static GrIrAccrualDto ToDto(GrIrAccrual a) => new(
        a.Id, a.GrnId, a.PoId, a.VendorId, a.GrnNumber,
        a.ReceivedOn, a.Amount, a.Currency, a.Status,
        a.InvoiceId, a.ClearedOn, a.ClearedBy,
        a.CreatedBy, a.CreatedAtUtc);
}