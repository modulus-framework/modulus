using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.TradeFinance.Domain.Entities;
using ProcureFlow.Modules.TradeFinance.Domain.Repositories;
using ProcureFlow.Modules.TradeFinance.Infrastructure.Database;

namespace ProcureFlow.Modules.TradeFinance.Infrastructure.Repositories;

public sealed class EfLcRepository(TradeFinanceDbContext db) : ILcRepository
{
    public Task<LetterOfCredit?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.LettersOfCredit
            .AsSplitQuery()
            .Include(l => l.Charges)
            .Include(l => l.Amendments)
            .Include(l => l.Presentations)
            .Include(l => l.MarginLedger)
            .Include(l => l.Maturities)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<LetterOfCredit?> GetByNumberAsync(Guid tenantId, string lcNumber, CancellationToken ct = default) =>
        db.LettersOfCredit
            .AsSplitQuery()
            .Include(l => l.Charges)
            .Include(l => l.Amendments)
            .Include(l => l.Presentations)
            .Include(l => l.MarginLedger)
            .Include(l => l.Maturities)
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.LcNumber == lcNumber, ct);

    public Task<IReadOnlyList<LetterOfCredit>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        db.LettersOfCredit
            .AsSplitQuery()
            .Include(l => l.Charges)
            .Include(l => l.Amendments)
            .Include(l => l.Presentations)
            .Include(l => l.MarginLedger)
            .Include(l => l.Maturities)
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.CreatedBy)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<LetterOfCredit>)t.Result, ct);

    public async Task AddAsync(LetterOfCredit lc, CancellationToken ct = default) =>
        await db.LettersOfCredit.AddAsync(lc, ct);

    public async Task SaveAsync(LetterOfCredit lc, CancellationToken ct = default) =>
        await Task.FromResult(db.LettersOfCredit.Update(lc));

    public Task<bool> ExistsByNumberAsync(Guid tenantId, string lcNumber, CancellationToken ct = default) =>
        db.LettersOfCredit.AnyAsync(l => l.TenantId == tenantId && l.LcNumber == lcNumber, ct);
}

public sealed class EfTtRepository(TradeFinanceDbContext db) : ITtRepository
{
    public Task<TtPayment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.TtPayments.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<TtPayment?> GetByNumberAsync(Guid tenantId, string ttNumber, CancellationToken ct = default) =>
        db.TtPayments.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TtNumber == ttNumber, ct);

    public Task<IReadOnlyList<TtPayment>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TtPayments
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.Status)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<TtPayment>)t.Result, ct);

    public async Task AddAsync(TtPayment tt, CancellationToken ct = default) =>
        await db.TtPayments.AddAsync(tt, ct);

    public async Task SaveAsync(TtPayment tt, CancellationToken ct = default) =>
        await Task.FromResult(db.TtPayments.Update(tt));

    public Task<bool> ExistsByNumberAsync(Guid tenantId, string ttNumber, CancellationToken ct = default) =>
        db.TtPayments.AnyAsync(t => t.TenantId == tenantId && t.TtNumber == ttNumber, ct);
}

public sealed class EfSwiftMessageRepository(TradeFinanceDbContext db) : ISwiftMessageRepository
{
    public async Task AddAsync(SwiftMessage message, CancellationToken ct = default) =>
        await db.SwiftMessages.AddAsync(message, ct);

    public Task<IReadOnlyList<SwiftMessage>> GetUnmatchedAsync(Guid tenantId, CancellationToken ct = default) =>
        db.SwiftMessages
            .Where(s => s.TenantId == tenantId && !s.IsMatched)
            .OrderBy(s => s.Reference)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SwiftMessage>)t.Result, ct);
}

public sealed class EfBankFacilityRepository(TradeFinanceDbContext db) : IBankFacilityRepository
{
    public Task<BankFacility?> GetByBankAsync(Guid tenantId, Guid bankId, CancellationToken ct = default) =>
        db.BankFacilities
            .AsSplitQuery()
            .Include(f => f.Entries)
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.BankId == bankId, ct);

    public async Task AddAsync(BankFacility facility, CancellationToken ct = default) =>
        await db.BankFacilities.AddAsync(facility, ct);

    public async Task SaveAsync(BankFacility facility, CancellationToken ct = default) =>
        await Task.FromResult(db.BankFacilities.Update(facility));
}

public sealed class EfPaymentObligationRepository(TradeFinanceDbContext db) : IPaymentObligationRepository
{
    public async Task AddAsync(PaymentObligation obligation, CancellationToken ct = default) =>
        await db.PaymentObligations.AddAsync(obligation, ct);

    public Task<IReadOnlyList<PaymentObligation>> GetUpcomingAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        db.PaymentObligations
            .Where(o => o.TenantId == tenantId && o.DueDate >= from && o.DueDate <= to)
            .OrderBy(o => o.DueDate)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<PaymentObligation>)t.Result, ct);

    public Task<IReadOnlyList<PaymentObligation>> GetOverdueAsync(Guid tenantId, DateOnly asOfDate, CancellationToken ct = default) =>
        db.PaymentObligations
            .Where(o => o.TenantId == tenantId && o.DueDate < asOfDate && o.Status == MaturityStatus.Open)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<PaymentObligation>)t.Result, ct);
}