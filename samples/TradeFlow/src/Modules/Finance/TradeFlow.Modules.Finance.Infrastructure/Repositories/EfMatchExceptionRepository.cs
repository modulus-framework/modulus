using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

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