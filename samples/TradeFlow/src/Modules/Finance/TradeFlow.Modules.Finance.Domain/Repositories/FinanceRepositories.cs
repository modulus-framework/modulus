using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Finance.Domain.Repositories;

public interface IApInvoiceRepository
{
    Task<ApInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ApInvoice>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct = default);
    Task<IReadOnlyList<ApInvoice>> GetByStatusAsync(ApInvoiceStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<ApInvoice>> GetOverdueInvoicesAsync(DateOnly asOfDate, CancellationToken ct = default);
    void Add(ApInvoice invoice);
    void Update(ApInvoice invoice);
    void Delete(ApInvoice invoice);
    Task<bool> ExistsByNumberAsync(string invoiceNumber, Guid? excludeId = null, CancellationToken ct = default);
}

public interface IFxRateRepository
{
    Task<FxRate?> GetEffectiveRateAsync(Guid tenantId, string fromCurrency, string toCurrency, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<FxRate>> GetByDateRangeAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);
    void Add(FxRate fxRate);
    void Update(FxRate fxRate);
    void Delete(FxRate fxRate);
}

public interface ICostCenterRepository
{
    Task<CostCenter?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CostCenter>> GetActiveAsync(Guid tenantId, CancellationToken ct = default);
    void Add(CostCenter costCenter);
    void Update(CostCenter costCenter);
    void Delete(CostCenter costCenter);
}

public interface IPaymentProposalRepository
{
    Task<PaymentProposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(PaymentProposal proposal);
    void Update(PaymentProposal proposal);
    void Delete(PaymentProposal proposal);
}

public interface IJournalBatchRepository
{
    Task<JournalBatch?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(JournalBatch journalBatch);
    void Update(JournalBatch journalBatch);
    void Delete(JournalBatch journalBatch);
}

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

public interface IMatchExceptionRepository
{
    Task<MatchException?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MatchException>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default);
    Task<IReadOnlyList<MatchException>> GetOpenAsync(CancellationToken ct = default);
    void Add(MatchException matchException);
    void Update(MatchException matchException);
}

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

public interface IGrIrAccrualRepository
{
    Task<GrIrAccrual?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<GrIrAccrual?> GetByGrnIdAsync(Guid grnId, CancellationToken ct = default);
    Task<IReadOnlyList<GrIrAccrual>> GetOpenAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GrIrAccrual>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct = default);
    void Add(GrIrAccrual accrual);
    void Update(GrIrAccrual accrual);
}