using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.SpendAnalysis.Application.Queries;
using TradeFlow.Modules.SpendAnalysis.Domain.Entities;
using TradeFlow.Modules.SpendAnalysis.Domain.Repositories;

namespace TradeFlow.Modules.SpendAnalysis.Application.Handlers;

// ── Category Query Handlers ──────────────────────────────────────────

public sealed class GetCategoryByIdQueryHandler : IQueryHandler<GetCategoryByIdQuery, CategoryTaxonomy?>
{
    private readonly ICategoryTaxonomyRepository _repository;
    public GetCategoryByIdQueryHandler(ICategoryTaxonomyRepository repository) => _repository = repository;

    public async Task<CategoryTaxonomy?> HandleAsync(GetCategoryByIdQuery request, CancellationToken ct)
        => await _repository.GetByIdAsync(request.CategoryId, ct);
}

public sealed class GetAllCategoriesQueryHandler : IQueryHandler<GetAllCategoriesQuery, IReadOnlyList<CategoryTaxonomy>>
{
    private readonly ICategoryTaxonomyRepository _repository;
    public GetAllCategoriesQueryHandler(ICategoryTaxonomyRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<CategoryTaxonomy>> HandleAsync(GetAllCategoriesQuery request, CancellationToken ct)
        => await _repository.GetAllAsync(Guid.Empty, ct);
}

public sealed class GetCategoryChildrenQueryHandler : IQueryHandler<GetCategoryChildrenQuery, IReadOnlyList<CategoryTaxonomy>>
{
    private readonly ICategoryTaxonomyRepository _repository;
    public GetCategoryChildrenQueryHandler(ICategoryTaxonomyRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<CategoryTaxonomy>> HandleAsync(GetCategoryChildrenQuery request, CancellationToken ct)
        => await _repository.GetChildrenAsync(request.ParentId, ct);
}

// ── Spend Analytics Query Handlers ───────────────────────────────────

public sealed class GetSpendByCategoryQueryHandler : IQueryHandler<GetSpendByCategoryQuery, IReadOnlyList<SpendCubeEntry>>
{
    private readonly ISpendCubeRepository _repository;
    public GetSpendByCategoryQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<SpendCubeEntry>> HandleAsync(GetSpendByCategoryQuery request, CancellationToken ct)
    {
        // Get all entries in the period range
        var all = new List<SpendCubeEntry>();
        for (var p = request.FromPeriod; p <= request.ToPeriod; p = p.AddMonths(1))
        {
            var entries = await _repository.GetByPeriodAsync(p, ct);
            all.AddRange(entries);
        }

        if (request.CategoryId.HasValue)
            all = all.Where(e => e.CategoryId == request.CategoryId.Value).ToList();

        return all;
    }
}

public sealed class GetSpendByVendorQueryHandler : IQueryHandler<GetSpendByVendorQuery, IReadOnlyList<SpendCubeEntry>>
{
    private readonly ISpendCubeRepository _repository;
    public GetSpendByVendorQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<SpendCubeEntry>> HandleAsync(GetSpendByVendorQuery request, CancellationToken ct)
        => await _repository.GetByVendorAsync(request.VendorId, request.FromPeriod, request.ToPeriod, ct);
}

public sealed class GetPriceVarianceQueryHandler : IQueryHandler<GetPriceVarianceQuery, IReadOnlyList<PriceVarianceDto>>
{
    private readonly ISpendCubeRepository _repository;
    public GetPriceVarianceQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<PriceVarianceDto>> HandleAsync(GetPriceVarianceQuery request, CancellationToken ct)
    {
        // Placeholder: actual implementation would join PO lines with price baselines
        // For now return empty list - to be implemented with real data
        await Task.CompletedTask;
        return Array.Empty<PriceVarianceDto>();
    }
}

public sealed class GetSavingsTrackerQueryHandler : IQueryHandler<GetSavingsTrackerQuery, IReadOnlyList<SavingsEntryDto>>
{
    private readonly ISpendCubeRepository _repository;
    public GetSavingsTrackerQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<SavingsEntryDto>> HandleAsync(GetSavingsTrackerQuery request, CancellationToken ct)
    {
        // Placeholder: actual implementation would compare negotiated vs baseline prices
        await Task.CompletedTask;
        return Array.Empty<SavingsEntryDto>();
    }
}

public sealed class GetTailSpendQueryHandler : IQueryHandler<GetTailSpendQuery, TailSpendDto>
{
    private readonly ISpendCubeRepository _repository;
    public GetTailSpendQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<TailSpendDto> HandleAsync(GetTailSpendQuery request, CancellationToken ct)
    {
        // Placeholder: actual implementation would identify bottom 80% vendors by spend
        await Task.CompletedTask;
        return new TailSpendDto(0, 0, 0, 0, 0, Array.Empty<VendorSpendSummaryDto>());
    }
}

public sealed class GetSingleSourceRiskQueryHandler : IQueryHandler<GetSingleSourceRiskQuery, IReadOnlyList<SingleSourceRiskDto>>
{
    private readonly ISpendCubeRepository _repository;
    public GetSingleSourceRiskQueryHandler(ISpendCubeRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<SingleSourceRiskDto>> HandleAsync(GetSingleSourceRiskQuery request, CancellationToken ct)
    {
        // Placeholder: actual implementation would find categories with >80% spend from one vendor
        await Task.CompletedTask;
        return Array.Empty<SingleSourceRiskDto>();
    }
}
