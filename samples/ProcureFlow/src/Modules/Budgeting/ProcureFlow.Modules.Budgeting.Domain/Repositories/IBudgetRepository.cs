using ProcureFlow.Modules.Budgeting.Domain.Entities;

namespace ProcureFlow.Modules.Budgeting.Domain.Repositories;

public interface IBudgetRepository
{
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Budget?> GetAsync(
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Guid tenantId,
        int fiscalYear,
        Guid costCenterId,
        string category,
        Guid? projectId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Budget>> GetAllAsync(
        Guid tenantId,
        int? fiscalYear,
        Guid? costCenterId,
        string? category,
        CancellationToken ct = default);

    Task AddAsync(Budget budget, CancellationToken ct = default);

    Task UpdateAsync(Budget budget, CancellationToken ct = default);
}