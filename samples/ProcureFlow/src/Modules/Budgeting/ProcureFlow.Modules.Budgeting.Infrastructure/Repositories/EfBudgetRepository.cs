using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Budgeting.Domain.Entities;
using ProcureFlow.Modules.Budgeting.Domain.Repositories;
using ProcureFlow.Modules.Budgeting.Infrastructure.Database;

namespace ProcureFlow.Modules.Budgeting.Infrastructure.Repositories;

public sealed class EfBudgetRepository(BudgetsDbContext context) : IBudgetRepository
{
    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Budgets
            .AsSplitQuery()
            .Include(b => b.Revisions)
            .Include(b => b.Ledger)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<Budget?> GetAsync(
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        CancellationToken ct = default)
    {
        return await context.Budgets
            .AsSplitQuery()
            .Include(b => b.Revisions)
            .Include(b => b.Ledger)
            .FirstOrDefaultAsync(b =>
                b.TenantId == tenantId &&
                b.FiscalYear == fiscalYear &&
                b.CostCenterId == costCenterId &&
                b.Category == category &&
                b.ProjectId == projectId, ct);
    }

    public async Task<bool> ExistsAsync(
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        CancellationToken ct = default)
    {
        return await context.Budgets.AnyAsync(b =>
            b.TenantId == tenantId &&
            b.FiscalYear == fiscalYear &&
            b.CostCenterId == costCenterId &&
            b.Category == category &&
            b.ProjectId == projectId, ct);
    }

    public async Task<IReadOnlyList<Budget>> GetAllAsync(
        Guid tenantId,
        int? fiscalYear,
        Guid? costCenterId,
        string? category,
        CancellationToken ct = default)
    {
        IQueryable<Budget> query = context.Budgets
            .Where(b => b.TenantId == tenantId);

        if (fiscalYear.HasValue)
            query = query.Where(b => b.FiscalYear == fiscalYear.Value);

        if (costCenterId.HasValue)
            query = query.Where(b => b.CostCenterId == costCenterId.Value);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(b => b.Category == category);

        return await query
            .OrderBy(b => b.FiscalYear)
            .ThenBy(b => b.Category)
            .AsSplitQuery()
            .Include(b => b.Revisions)
            .Include(b => b.Ledger)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Budget budget, CancellationToken ct = default)
    {
        await context.Budgets.AddAsync(budget, ct);
    }

    public async Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        context.Budgets.Update(budget);
        await Task.CompletedTask;
    }
}