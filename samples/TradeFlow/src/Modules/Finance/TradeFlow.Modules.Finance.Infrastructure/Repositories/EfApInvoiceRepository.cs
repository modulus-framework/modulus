using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

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

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────
