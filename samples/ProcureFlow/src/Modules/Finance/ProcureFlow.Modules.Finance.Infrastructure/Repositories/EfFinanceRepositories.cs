using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Finance.Domain.Entities;
using ProcureFlow.Modules.Finance.Domain.Repositories;
using ProcureFlow.Modules.Finance.Infrastructure.Database;

namespace ProcureFlow.Modules.Finance.Infrastructure.Repositories;

public sealed class EfApInvoiceRepository : IApInvoiceRepository
{
    private readonly FinanceDbContext _context;

    public EfApInvoiceRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<ApInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.ApInvoices
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<ApInvoice>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct = default)
    {
        return await _context.ApInvoices
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .Where(x => x.VendorId == vendorId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApInvoice>> GetByStatusAsync(ApInvoiceStatus status, CancellationToken ct = default)
    {
        return await _context.ApInvoices
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .Where(x => x.Status == status)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApInvoice>> GetOverdueInvoicesAsync(DateOnly asOfDate, CancellationToken ct = default)
    {
        return await _context.ApInvoices
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .Where(x => x.DueDate < asOfDate
                && x.Status == ApInvoiceStatus.Approved
                && x.TotalAmount > x.Payments.Where(p => p.Status == PaymentStatus.Cleared).Sum(p => p.Amount))
            .ToListAsync(ct);
    }

    public void Add(ApInvoice invoice) => _context.ApInvoices.Add(invoice);

    public void Update(ApInvoice invoice) => _context.ApInvoices.Update(invoice);

    public void Delete(ApInvoice invoice) => _context.ApInvoices.Remove(invoice);

    public async Task<bool> ExistsByNumberAsync(string invoiceNumber, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = _context.ApInvoices.Where(x => x.InvoiceNumber == invoiceNumber);
        if (excludeId.HasValue)
            query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }
}

public sealed class EfFxRateRepository : IFxRateRepository
{
    private readonly FinanceDbContext _context;

    public EfFxRateRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<FxRate?> GetEffectiveRateAsync(Guid tenantId, string fromCurrency, string toCurrency, DateOnly asOfDate, CancellationToken ct = default)
    {
        return await _context.FxRates
            .Where(x => x.FromCurrency == fromCurrency && x.ToCurrency == toCurrency && x.EffectiveDate <= asOfDate)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<FxRate>> GetByDateRangeAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        var query = _context.FxRates.AsQueryable();
        if (fromDate.HasValue)
            query = query.Where(x => x.EffectiveDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(x => x.EffectiveDate <= toDate.Value);
        return await query.OrderBy(x => x.EffectiveDate).ToListAsync(ct);
    }

    public void Add(FxRate fxRate) => _context.FxRates.Add(fxRate);

    public void Update(FxRate fxRate) => _context.FxRates.Update(fxRate);

    public void Delete(FxRate fxRate) => _context.FxRates.Remove(fxRate);
}

public sealed class EfCostCenterRepository : ICostCenterRepository
{
    private readonly FinanceDbContext _context;

    public EfCostCenterRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<CostCenter?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.CostCenters.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<CostCenter>> GetActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.CostCenters
            .Where(x => x.IsActive)
            .ToListAsync(ct);
    }

    public void Add(CostCenter costCenter) => _context.CostCenters.Add(costCenter);

    public void Update(CostCenter costCenter) => _context.CostCenters.Update(costCenter);

    public void Delete(CostCenter costCenter) => _context.CostCenters.Remove(costCenter);
}

public sealed class EfPaymentProposalRepository : IPaymentProposalRepository
{
    private readonly FinanceDbContext _context;

    public EfPaymentProposalRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentProposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.PaymentProposals.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public void Add(PaymentProposal proposal) => _context.PaymentProposals.Add(proposal);

    public void Update(PaymentProposal proposal) => _context.PaymentProposals.Update(proposal);

    public void Delete(PaymentProposal proposal) => _context.PaymentProposals.Remove(proposal);
}

public sealed class EfJournalBatchRepository : IJournalBatchRepository
{
    private readonly FinanceDbContext _context;

    public EfJournalBatchRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<JournalBatch?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.JournalBatches
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public void Add(JournalBatch journalBatch) => _context.JournalBatches.Add(journalBatch);

    public void Update(JournalBatch journalBatch) => _context.JournalBatches.Update(journalBatch);

    public void Delete(JournalBatch journalBatch) => _context.JournalBatches.Remove(journalBatch);
}

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

public sealed class EfMatchExceptionRepository : IMatchExceptionRepository
{
    private readonly FinanceDbContext _context;

    public EfMatchExceptionRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<MatchException?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.MatchExceptions.FindAsync([id], ct);
    }

    public async Task<IReadOnlyList<MatchException>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        return await _context.MatchExceptions
            .Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MatchException>> GetOpenAsync(CancellationToken ct = default)
    {
        return await _context.MatchExceptions
            .Where(x => x.Status == MatchExceptionStatus.Open)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public void Add(MatchException matchException) => _context.MatchExceptions.Add(matchException);

    public void Update(MatchException matchException) => _context.MatchExceptions.Update(matchException);
}

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

public sealed class EfGrIrAccrualRepository : IGrIrAccrualRepository
{
    private readonly FinanceDbContext _context;

    public EfGrIrAccrualRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<GrIrAccrual?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals.FindAsync([id], ct);
    }

    public async Task<GrIrAccrual?> GetByGrnIdAsync(Guid grnId, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals.FirstOrDefaultAsync(x => x.GrnId == grnId, ct);
    }

    public async Task<IReadOnlyList<GrIrAccrual>> GetOpenAsync(CancellationToken ct = default)
    {
        return await _context.GrIrAccruals
            .Where(x => x.Status == GrIrAccrualStatus.Open)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GrIrAccrual>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct = default)
    {
        return await _context.GrIrAccruals
            .Where(x => x.VendorId == vendorId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public void Add(GrIrAccrual accrual) => _context.GrIrAccruals.Add(accrual);

    public void Update(GrIrAccrual accrual) => _context.GrIrAccruals.Update(accrual);
}