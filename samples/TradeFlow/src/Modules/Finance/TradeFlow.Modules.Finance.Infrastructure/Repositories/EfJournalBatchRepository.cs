using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

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