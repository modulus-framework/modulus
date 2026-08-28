using ProcureFlow.Modules.Inventory.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Inventory.Application.Queries;

public sealed record GetStockItemQuery(Guid SiteId, Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<StockItemResponse>>;

public sealed record GetGrnQuery(Guid GrnId) : Modulus.Mediator.Abstractions.IQuery<Result<GrnResponse>>;

public sealed record GetQcInspectionQuery(Guid GrnId) : Modulus.Mediator.Abstractions.IQuery<Result<QcInspectionResponse>>;

public sealed record ListBatchesQuery(Guid SiteId, Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<BatchResponse>>>;

public sealed record ListLedgerEntriesQuery(Guid SiteId, Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<InventoryValueLedgerEntryResponse>>>;

// ── GRN Return Draft (BR-GRN-02) ────────────────────────────────────

public sealed record GetReturnDraftQuery(Guid DraftId) : Modulus.Mediator.Abstractions.IQuery<Result<GrnReturnDraftResponse>>;

public sealed record ListReturnDraftsByGrnQuery(Guid GrnId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<GrnReturnDraftResponse>>>;
