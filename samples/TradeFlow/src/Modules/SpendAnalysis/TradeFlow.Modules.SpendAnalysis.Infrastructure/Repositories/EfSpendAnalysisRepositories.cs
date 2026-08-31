using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.SpendAnalysis.Domain.Entities;
using TradeFlow.Modules.SpendAnalysis.Domain.Repositories;
using TradeFlow.Modules.SpendAnalysis.Infrastructure.Database;

namespace TradeFlow.Modules.SpendAnalysis.Infrastructure.Repositories;

public sealed class EfCategoryTaxonomyRepository : ICategoryTaxonomyRepository
{
    private readonly SpendAnalysisDbContext _db;

    public EfCategoryTaxonomyRepository(SpendAnalysisDbContext db) => _db = db;

    public async Task<CategoryTaxonomy?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Categories.FindAsync([id], ct);

    public async Task<CategoryTaxonomy?> GetByCodeAsync(string code, Guid tenantId, CancellationToken ct)
        => await _db.Categories.FirstOrDefaultAsync(c => c.Code == code && c.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(Guid tenantId, CancellationToken ct)
        => await _db.Categories.Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.Code).ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryTaxonomy>> GetChildrenAsync(Guid parentId, CancellationToken ct)
        => await _db.Categories.Where(c => c.ParentId == parentId && c.IsActive)
            .OrderBy(c => c.Code).ToListAsync(ct);

    public void Add(CategoryTaxonomy category) => _db.Categories.Add(category);

    public void Update(CategoryTaxonomy category) => _db.Categories.Update(category);
}

public sealed class EfPoLineCategoryMappingRepository : IPoLineCategoryMappingRepository
{
    private readonly SpendAnalysisDbContext _db;

    public EfPoLineCategoryMappingRepository(SpendAnalysisDbContext db) => _db = db;

    public async Task<PoLineCategoryMapping?> GetByPoLineIdAsync(Guid poLineId, CancellationToken ct)
        => await _db.PoLineCategoryMappings.FirstOrDefaultAsync(m => m.PoLineId == poLineId, ct);

    public void Add(PoLineCategoryMapping mapping) => _db.PoLineCategoryMappings.Add(mapping);
}

public sealed class EfSpendCubeRepository : ISpendCubeRepository
{
    private readonly SpendAnalysisDbContext _db;

    public EfSpendCubeRepository(SpendAnalysisDbContext db) => _db = db;

    public async Task<IReadOnlyList<SpendCubeEntry>> GetByPeriodAsync(DateOnly period, CancellationToken ct)
        => await _db.SpendCubeEntries.Where(s => s.Period == period).ToListAsync(ct);

    public async Task<IReadOnlyList<SpendCubeEntry>> GetByVendorAsync(Guid vendorId, DateOnly from, DateOnly to, CancellationToken ct)
        => await _db.SpendCubeEntries.Where(s => s.VendorId == vendorId && s.Period >= from && s.Period <= to)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SpendCubeEntry>> GetByCategoryAsync(Guid categoryId, DateOnly from, DateOnly to, CancellationToken ct)
        => await _db.SpendCubeEntries.Where(s => s.CategoryId == categoryId && s.Period >= from && s.Period <= to)
            .ToListAsync(ct);

    public void AddOrUpdateRange(IReadOnlyList<SpendCubeEntry> entries)
    {
        _db.SpendCubeEntries.AddRange(entries);
    }
}

public sealed class EfSpendAnalysisUnitOfWork : ISpendAnalysisUnitOfWork
{
    private readonly SpendAnalysisDbContext _db;

    public EfSpendAnalysisUnitOfWork(SpendAnalysisDbContext db)
    {
        _db = db;
        Categories = new EfCategoryTaxonomyRepository(db);
        PoLineCategoryMappings = new EfPoLineCategoryMappingRepository(db);
        SpendCube = new EfSpendCubeRepository(db);
    }

    public ICategoryTaxonomyRepository Categories { get; }
    public IPoLineCategoryMappingRepository PoLineCategoryMappings { get; }
    public ISpendCubeRepository SpendCube { get; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
