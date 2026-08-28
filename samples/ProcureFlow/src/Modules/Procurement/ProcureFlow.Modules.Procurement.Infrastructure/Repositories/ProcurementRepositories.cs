using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Modules.Procurement.Domain.Repositories;
using ProcureFlow.Modules.Procurement.Infrastructure.Database;

namespace ProcureFlow.Modules.Procurement.Infrastructure.Repositories;

public sealed class EfPrRepository(ProcurementDbContext context) : IPrRepository
{
    public async Task<PurchaseRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.PurchaseRequisitions.AsSplitQuery().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PurchaseRequisition?> GetByNumberAsync(Guid tenantId, string prNumber, CancellationToken ct = default)
        => context.PurchaseRequisitions.AsSplitQuery().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PrNumber == prNumber, ct);

    public async Task<IReadOnlyList<PurchaseRequisition>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
        => await context.PurchaseRequisitions.AsSplitQuery().Include(p => p.Lines)
            .Where(p => p.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(PurchaseRequisition pr, CancellationToken ct = default)
        => await context.PurchaseRequisitions.AddAsync(pr, ct);

    public Task SaveAsync(PurchaseRequisition pr, CancellationToken ct = default)
    {
        context.PurchaseRequisitions.Update(pr);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsByNumberAsync(Guid tenantId, string prNumber, CancellationToken ct = default)
        => context.PurchaseRequisitions.AnyAsync(p => p.TenantId == tenantId && p.PrNumber == prNumber, ct);
}

public sealed class EfRfqRepository(ProcurementDbContext context) : IRfqRepository
{
    public async Task<Rfq?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Rfqs
            .AsSplitQuery()
            .Include(r => r.Lines)
            .Include(r => r.Invitations)
            .Include(r => r.Bids)
            .Include(r => r.Comparison)
            .Include(r => r.Award)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Rfq?> GetByNumberAsync(Guid tenantId, string rfqNumber, CancellationToken ct = default)
        => context.Rfqs.AsSplitQuery()
            .Include(r => r.Lines).Include(r => r.Invitations).Include(r => r.Bids)
            .Include(r => r.Comparison).Include(r => r.Award)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RfqNumber == rfqNumber, ct);

    public async Task AddAsync(Rfq rfq, CancellationToken ct = default)
        => await context.Rfqs.AddAsync(rfq, ct);

    public Task SaveAsync(Rfq rfq, CancellationToken ct = default)
    {
        context.Rfqs.Update(rfq);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsByNumberAsync(Guid tenantId, string rfqNumber, CancellationToken ct = default)
        => context.Rfqs.AnyAsync(r => r.TenantId == tenantId && r.RfqNumber == rfqNumber, ct);
}

public sealed class EfPoRepository(ProcurementDbContext context) : IPoRepository
{
    public async Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.PurchaseOrders
            .AsSplitQuery()
            .Include(p => p.Lines)
            .Include(p => p.Revisions)
            .Include(p => p.Feasibility)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PurchaseOrder?> GetByNumberAsync(Guid tenantId, string poNumber, CancellationToken ct = default)
        => context.PurchaseOrders.AsSplitQuery()
            .Include(p => p.Lines).Include(p => p.Revisions).Include(p => p.Feasibility)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PoNumber == poNumber, ct);

    public async Task<IReadOnlyList<PurchaseOrder>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
        => await context.PurchaseOrders.AsSplitQuery()
            .Include(p => p.Lines).Include(p => p.Revisions).Include(p => p.Feasibility)
            .Where(p => p.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(PurchaseOrder po, CancellationToken ct = default)
        => await context.PurchaseOrders.AddAsync(po, ct);

    public Task SaveAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        context.PurchaseOrders.Update(po);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsByNumberAsync(Guid tenantId, string poNumber, CancellationToken ct = default)
        => context.PurchaseOrders.AnyAsync(p => p.TenantId == tenantId && p.PoNumber == poNumber, ct);
}