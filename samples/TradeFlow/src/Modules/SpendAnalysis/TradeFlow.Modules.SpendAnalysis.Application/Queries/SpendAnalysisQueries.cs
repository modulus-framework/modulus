using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.SpendAnalysis.Domain.Entities;

namespace TradeFlow.Modules.SpendAnalysis.Application.Queries;

// ── Category Queries ─────────────────────────────────────────────────

public sealed record GetCategoryByIdQuery(Guid CategoryId) : IQuery<CategoryTaxonomy?>;

public sealed record GetAllCategoriesQuery() : IQuery<IReadOnlyList<CategoryTaxonomy>>;

public sealed record GetCategoryChildrenQuery(Guid ParentId) : IQuery<IReadOnlyList<CategoryTaxonomy>>;

// ── Spend Analytics Queries (BR-SA-02..06) ───────────────────────────

/// <summary>BR-SA-02: Spend by category/vendor/BU/site/month.</summary>
public sealed record GetSpendByCategoryQuery(
    DateOnly FromPeriod,
    DateOnly ToPeriod,
    Guid? CategoryId = null
) : IQuery<IReadOnlyList<SpendCubeEntry>>;

public sealed record GetSpendByVendorQuery(
    Guid VendorId,
    DateOnly FromPeriod,
    DateOnly ToPeriod
) : IQuery<IReadOnlyList<SpendCubeEntry>>;

/// <summary>BR-SA-03: Price variance (PPV) vs. last/contract/standard.</summary>
public sealed record GetPriceVarianceQuery(
    DateOnly FromPeriod,
    DateOnly ToPeriod,
    Guid? VendorId = null,
    Guid? CategoryId = null
) : IQuery<IReadOnlyList<PriceVarianceDto>>;

public sealed record PriceVarianceDto(
    Guid PoLineId,
    string ItemDescription,
    decimal ActualPrice,
    decimal BaselinePrice,
    decimal Variance,
    decimal VariancePercent,
    string BaselineType,  // "LastPO", "Contract", "Standard"
    Guid VendorId,
    string VendorName
);

/// <summary>BR-SA-04: Savings tracker (negotiated vs. baseline).</summary>
public sealed record GetSavingsTrackerQuery(
    DateOnly FromPeriod,
    DateOnly ToPeriod,
    int TopN = 10
) : IQuery<IReadOnlyList<SavingsEntryDto>>;

public sealed record SavingsEntryDto(
    Guid VendorId,
    string VendorName,
    decimal NegotiatedAmount,
    decimal BaselineAmount,
    decimal Savings,
    decimal SavingsPercent
);

/// <summary>BR-SA-05: Tail-spend identification (bottom 80% vendors by spend).</summary>
public sealed record GetTailSpendQuery(
    DateOnly FromPeriod,
    DateOnly ToPeriod
) : IQuery<TailSpendDto>;

public sealed record TailSpendDto(
    int TotalVendorCount,
    int TailVendorCount,
    decimal TailSpendAmount,
    decimal TotalSpend,
    decimal TailSpendPercent,
    IReadOnlyList<VendorSpendSummaryDto> TopTailVendors
);

public sealed record VendorSpendSummaryDto(
    Guid VendorId,
    string VendorName,
    decimal TotalSpend,
    int PoCount
);

/// <summary>BR-SA-06: Single-source risk exposure.</summary>
public sealed record GetSingleSourceRiskQuery(
    DateOnly FromPeriod,
    DateOnly ToPeriod,
    decimal ThresholdPercent = 80m
) : IQuery<IReadOnlyList<SingleSourceRiskDto>>;

public sealed record SingleSourceRiskDto(
    string CategoryCode,
    string CategoryName,
    Guid PrimaryVendorId,
    string PrimaryVendorName,
    decimal PrimaryVendorSpend,
    decimal TotalCategorySpend,
    decimal VendorSharePercent
);
