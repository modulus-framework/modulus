using TradeFlow.Modules.SpendAnalysis.Domain.Entities;

namespace TradeFlow.Modules.SpendAnalysis.Domain.Repositories;

public interface ICategoryTaxonomyRepository
{
    Task<CategoryTaxonomy?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CategoryTaxonomy?> GetByCodeAsync(string code, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CategoryTaxonomy>> GetChildrenAsync(Guid parentId, CancellationToken ct);
    void Add(CategoryTaxonomy category);
    void Update(CategoryTaxonomy category);
}

public interface IPoLineCategoryMappingRepository
{
    Task<PoLineCategoryMapping?> GetByPoLineIdAsync(Guid poLineId, CancellationToken ct);
    void Add(PoLineCategoryMapping mapping);
}

public interface ISpendCubeRepository
{
    Task<IReadOnlyList<SpendCubeEntry>> GetByPeriodAsync(DateOnly period, CancellationToken ct);
    Task<IReadOnlyList<SpendCubeEntry>> GetByVendorAsync(Guid vendorId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<SpendCubeEntry>> GetByCategoryAsync(Guid categoryId, DateOnly from, DateOnly to, CancellationToken ct);
    void AddOrUpdateRange(IReadOnlyList<SpendCubeEntry> entries);
}

public interface ISpendAnalysisUnitOfWork
{
    ICategoryTaxonomyRepository Categories { get; }
    IPoLineCategoryMappingRepository PoLineCategoryMappings { get; }
    ISpendCubeRepository SpendCube { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
