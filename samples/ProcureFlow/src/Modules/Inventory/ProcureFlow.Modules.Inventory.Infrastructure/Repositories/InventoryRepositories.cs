using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Inventory.Domain.Entities;
using ProcureFlow.Modules.Inventory.Domain.Repositories;
using ProcureFlow.Modules.Inventory.Infrastructure.Database;

namespace ProcureFlow.Modules.Inventory.Infrastructure.Repositories;

public sealed class EfStockItemRepository(InventoryDbContext db) : IStockItemRepository
{
    public Task<StockItem?> GetByItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default) =>
        db.StockItems.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SiteId == siteId && s.ItemId == itemId, ct);

    public async Task AddAsync(StockItem item, CancellationToken ct = default) =>
        await db.StockItems.AddAsync(item, ct);

    public async Task SaveAsync(StockItem item, CancellationToken ct = default) =>
        await Task.FromResult(db.StockItems.Update(item));
}

public sealed class EfGrnRepository(InventoryDbContext db) : IGrnRepository
{
    public Task<Grn?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Grns.AsSplitQuery().Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<Grn?> GetByNumberAsync(Guid tenantId, string grnNumber, CancellationToken ct = default) =>
        db.Grns.AsSplitQuery().Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.GrnNumber == grnNumber, ct);

    public async Task AddAsync(Grn grn, CancellationToken ct = default) =>
        await db.Grns.AddAsync(grn, ct);

    public async Task SaveAsync(Grn grn, CancellationToken ct = default) =>
        await Task.FromResult(db.Grns.Update(grn));
}

public sealed class EfQcInspectionRepository(InventoryDbContext db) : IQcInspectionRepository
{
    public Task<QcInspection?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.QcInspections.AsSplitQuery().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct);

    public Task<QcInspection?> GetByGrnAsync(Guid grnId, CancellationToken ct = default) =>
        db.QcInspections.AsSplitQuery().Include(q => q.Lines).FirstOrDefaultAsync(q => q.GrnId == grnId, ct);

    public async Task AddAsync(QcInspection inspection, CancellationToken ct = default) =>
        await db.QcInspections.AddAsync(inspection, ct);
}

public sealed class EfBatchRepository(InventoryDbContext db) : IBatchRepository
{
    public Task<IReadOnlyList<Batch>> GetForItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default) =>
        db.Batches
            .Where(b => b.TenantId == tenantId && b.SiteId == siteId && b.ItemId == itemId)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<Batch>)t.Result, ct);

    public async Task AddAsync(Batch batch, CancellationToken ct = default) =>
        await db.Batches.AddAsync(batch, ct);
}

public sealed class EfInventoryValueLedgerRepository(InventoryDbContext db) : IInventoryValueLedgerRepository
{
    public Task<IReadOnlyList<InventoryValueLedgerEntry>> GetForItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default) =>
        db.LedgerEntries
            .Where(e => e.TenantId == tenantId && e.SiteId == siteId && e.ItemId == itemId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<InventoryValueLedgerEntry>)t.Result, ct);

    public async Task AddAsync(InventoryValueLedgerEntry entry, CancellationToken ct = default) =>
        await db.LedgerEntries.AddAsync(entry, ct);
}

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed class EfGrnReturnDraftRepository(InventoryDbContext db) : IGrnReturnDraftRepository
{
    public Task<GrnReturnDraft?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ReturnDrafts.AsSplitQuery().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<IReadOnlyList<GrnReturnDraft>> GetByGrnIdAsync(Guid grnId, CancellationToken ct = default) =>
        db.ReturnDrafts.AsSplitQuery().Include(r => r.Lines)
            .Where(r => r.GrnId == grnId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<GrnReturnDraft>)t.Result, ct);

    public async Task AddAsync(GrnReturnDraft draft, CancellationToken ct = default) =>
        await db.ReturnDrafts.AddAsync(draft, ct);

    public async Task SaveAsync(GrnReturnDraft draft, CancellationToken ct = default) =>
        await Task.FromResult(db.ReturnDrafts.Update(draft));
}