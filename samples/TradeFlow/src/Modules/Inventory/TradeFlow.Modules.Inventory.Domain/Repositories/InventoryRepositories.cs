using TradeFlow.Modules.Inventory.Domain.Entities;

namespace TradeFlow.Modules.Inventory.Domain.Repositories;

public interface IStockItemRepository
{
    Task<StockItem?> GetByItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default);
    Task AddAsync(StockItem item, CancellationToken ct = default);
    Task SaveAsync(StockItem item, CancellationToken ct = default);
}

public interface IGrnRepository
{
    Task<Grn?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Grn?> GetByNumberAsync(Guid tenantId, string grnNumber, CancellationToken ct = default);
    Task AddAsync(Grn grn, CancellationToken ct = default);
    Task SaveAsync(Grn grn, CancellationToken ct = default);
}

public interface IQcInspectionRepository
{
    Task<QcInspection?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<QcInspection?> GetByGrnAsync(Guid grnId, CancellationToken ct = default);
    Task AddAsync(QcInspection inspection, CancellationToken ct = default);
}

public interface IBatchRepository
{
    Task<IReadOnlyList<Batch>> GetForItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default);
    Task AddAsync(Batch batch, CancellationToken ct = default);
}

public interface IInventoryValueLedgerRepository
{
    Task<IReadOnlyList<InventoryValueLedgerEntry>> GetForItemAsync(Guid tenantId, Guid siteId, Guid itemId, CancellationToken ct = default);
    Task AddAsync(InventoryValueLedgerEntry entry, CancellationToken ct = default);
}

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public interface IGrnReturnDraftRepository
{
    Task<GrnReturnDraft?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GrnReturnDraft>> GetByGrnIdAsync(Guid grnId, CancellationToken ct = default);
    Task AddAsync(GrnReturnDraft draft, CancellationToken ct = default);
    Task SaveAsync(GrnReturnDraft draft, CancellationToken ct = default);
}