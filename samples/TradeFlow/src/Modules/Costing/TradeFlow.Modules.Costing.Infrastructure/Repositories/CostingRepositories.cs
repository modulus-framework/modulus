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

    public Task<IReadOnlyList<LandedCostSheet>> GetFinalizedByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.LandedCostSheets
            .AsSplitQuery()
            .Include(s => s.Lines)
            .ThenInclude(l => l.Allocations)
            .Include(s => s.Elements)
            .Where(s => s.TenantId == tenantId &&
                        (s.Status == CostSheetStatus.Finalized || s.Status == CostSheetStatus.Adjusted))
            .OrderBy(s => s.FinalizedAtUtc ?? DateTime.UtcNow)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<LandedCostSheet>)t.Result, ct);

    public async Task<IReadOnlyList<LandedCostSheet>> GetFinalizedBetweenAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        DateTime fromUtc = from.ToDateTime(TimeOnly.MinValue);
        DateTime toExclusiveUtc = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        return await db.LandedCostSheets
            .AsSplitQuery()
            .Include(s => s.Lines)
            .ThenInclude(l => l.Allocations)
            .Include(s => s.Elements)
            .Where(s => s.TenantId == tenantId &&
                        (s.Status == CostSheetStatus.Finalized || s.Status == CostSheetStatus.Adjusted) &&
                        s.FinalizedAtUtc >= fromUtc && s.FinalizedAtUtc < toExclusiveUtc)
            .OrderBy(s => s.FinalizedAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddAsync(LandedCostSheet sheet, CancellationToken ct = default) =>
        await db.LandedCostSheets.AddAsync(sheet, ct);

    public async Task SaveAsync(LandedCostSheet sheet, CancellationToken ct = default) =>
        await Task.FromResult(db.LandedCostSheets.Update(sheet));
}

public sealed class EfRevaluationRunRepository(CostingDbContext db) : IRevaluationRunRepository
{
    public Task<RevaluationRun?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.RevaluationRuns
            .AsSplitQuery()
            .Include(r => r.Variances)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<IReadOnlyList<RevaluationRun>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        db.RevaluationRuns
            .AsSplitQuery()
            .Include(r => r.Variances)
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.StartedAtUtc)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<RevaluationRun>)t.Result, ct);

    public async Task AddAsync(RevaluationRun run, CancellationToken ct = default) =>
        await db.RevaluationRuns.AddAsync(run, ct);

    public async Task SaveAsync(RevaluationRun run, CancellationToken ct = default) =>
        await Task.FromResult(db.RevaluationRuns.Update(run));
}