using ProcureFlow.Modules.Procurement.Domain.Entities;

namespace ProcureFlow.Modules.Procurement.Domain.Repositories;

public interface IPrRepository
{
    Task<PurchaseRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PurchaseRequisition?> GetByNumberAsync(Guid tenantId, string prNumber, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseRequisition>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(PurchaseRequisition pr, CancellationToken ct = default);
    Task SaveAsync(PurchaseRequisition pr, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid tenantId, string prNumber, CancellationToken ct = default);
}

public interface IRfqRepository
{
    Task<Rfq?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Rfq?> GetByNumberAsync(Guid tenantId, string rfqNumber, CancellationToken ct = default);
    Task AddAsync(Rfq rfq, CancellationToken ct = default);
    Task SaveAsync(Rfq rfq, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid tenantId, string rfqNumber, CancellationToken ct = default);
}

public interface IPoRepository
{
    Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PurchaseOrder?> GetByNumberAsync(Guid tenantId, string poNumber, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrder>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(PurchaseOrder po, CancellationToken ct = default);
    Task SaveAsync(PurchaseOrder po, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid tenantId, string poNumber, CancellationToken ct = default);
}

public interface IContractRepository
{
    Task<Contract?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> GetAllAsync(Guid tenantId, ContractStatus? status, Guid? vendorId, CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> GetExpiringAsync(Guid tenantId, int withinDays, CancellationToken ct = default);
    Task<Contract?> GetActiveByVendorAndItemAsync(Guid vendorId, Guid itemId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task UpdateAsync(Contract contract, CancellationToken ct = default);
}