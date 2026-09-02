using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

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