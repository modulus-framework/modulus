using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Modules.Costing.Infrastructure.Database;

namespace TradeFlow.Modules.Costing.Infrastructure.Repositories;

public sealed class EfLandedCostSheetRepository(CostingDbContext db) : ILandedCostSheetRepository
{
    public Task<LandedCostSheet?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.LandedCostSheets
            .AsSplitQuery()
            .Include(s => s.Lines)
            .ThenInclude(l => l.Allocations)
            .Include(s => s.Elements)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<LandedCostSheet?> GetByFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        db.LandedCostSheets
            .AsSplitQuery()
            .Include(s => s.Lines)
            .ThenInclude(l => l.Allocations)
            .Include(s => s.Elements)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.FileId == fileId, ct);

    public Task<IReadOnlyList<LandedCostSheet>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.LandedCostSheets
            .AsSplitQuery()
            .Include(s => s.Lines)
            .ThenInclude(l => l.Allocations)
            .Include(s => s.Elements)
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.FinalizedAtUtc)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<LandedCostSheet>)t.Result, ct);

    public async Task AddAsync(LandedCostSheet sheet, CancellationToken ct = default) =>
        await db.LandedCostSheets.AddAsync(sheet, ct);

    public async Task SaveAsync(LandedCostSheet sheet, CancellationToken ct = default) =>
        await Task.FromResult(db.LandedCostSheets.Update(sheet));
}