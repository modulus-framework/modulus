using ProcureFlow.Modules.SpendAnalysis.Domain.Entities;

namespace ProcureFlow.Modules.SpendAnalysis.Domain.Repositories;

public interface ICategoryTaxonomyRepository
{
    Task<CategoryTaxonomy?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CategoryTaxonomy?> GetByCodeAsync(string code, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CategoryTaxonomy>> GetChildrenAsync(Guid parentId, CancellationToken ct);
    void Add(CategoryTaxonomy category);
    void Update(CategoryTaxonomy category);
}

public interface ISpendCubeRepository
{
    Task<IReadOnlyList<SpendCubeEntry>> GetByPeriodAsync(DateOnly period, CancellationToken ct);
    Task<IReadOnlyList<SpendCubeEntry>> GetByVendorAsync(Guid vendorId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<SpendCubeEntry>> GetByCategoryAsync(Guid categoryId, DateOnly from, DateOnly to, CancellationToken ct);
    void AddOrUpdateRange(IReadOnlyList<SpendCubeEntry> entries);
}

public interface ISpendAnalysisUnitOfWork : Modulus.Mediator.Abstractions.IUnitOfWork
{
    ICategoryTaxonomyRepository Categories { get; }
    ISpendCubeRepository SpendCube { get; }
}
