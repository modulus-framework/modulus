using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Finance.Domain.Entities;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;

namespace TradeFlow.Modules.Finance.Infrastructure.Repositories;

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