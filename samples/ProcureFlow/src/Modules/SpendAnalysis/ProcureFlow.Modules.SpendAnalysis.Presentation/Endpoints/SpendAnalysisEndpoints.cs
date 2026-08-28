using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.SpendAnalysis.Application.Commands;
using ProcureFlow.Modules.SpendAnalysis.Application.Queries;
using ProcureFlow.Modules.SpendAnalysis.Domain.Entities;

namespace ProcureFlow.Modules.SpendAnalysis.Presentation.Endpoints;

internal static class SpendAnalysisTags
{
    internal const string CategoryTaxonomy = "Category Taxonomy";
    internal const string SpendAnalytics = "Spend Analytics";
}

// ── Category Taxonomy CRUD ───────────────────────────────────────────

internal sealed class AddCategoryEndpoint : Endpoint<AddCategoryEndpoint.Request, Guid>
{
    private readonly IMediator _mediator;

    public AddCategoryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/categories");
        Tag(SpendAnalysisTags.CategoryTaxonomy);
        Summary("Create a new category taxonomy node (BR-SA-01)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new AddCategoryCommand(req.Code, req.Name, req.Description, req.ParentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ParentId { get; set; }
    }
}

internal sealed class UpdateCategoryEndpoint : Endpoint<UpdateCategoryEndpoint.Request>
{
    private readonly IMediator _mediator;

    public UpdateCategoryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/categories/{categoryId}");
        Tag(SpendAnalysisTags.CategoryTaxonomy);
        Summary("Update a category taxonomy node");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(
            new UpdateCategoryCommand(req.CategoryId, req.Name, req.Description, req.ParentId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? ParentId { get; set; }
    }
}

internal sealed class GetCategoryByIdEndpoint : Endpoint<GetCategoryByIdEndpoint.Request, CategoryTaxonomy>
{
    private readonly IMediator _mediator;

    public GetCategoryByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/categories/{categoryId}");
        Tag(SpendAnalysisTags.CategoryTaxonomy);
        Summary("Get a category by ID");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        CategoryTaxonomy? category = await _mediator.QueryAsync<CategoryTaxonomy?>(
            new GetCategoryByIdQuery(req.CategoryId), ct);
        if (category is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        await SendOkAsync(category, ct);
    }

    internal sealed class Request { public Guid CategoryId { get; set; } }
}

internal sealed class GetAllCategoriesEndpoint : Endpoint<GetAllCategoriesEndpoint.Request, IReadOnlyList<CategoryTaxonomy>>
{
    private readonly IMediator _mediator;

    public GetAllCategoriesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/categories");
        Tag(SpendAnalysisTags.CategoryTaxonomy);
        Summary("List all categories");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<CategoryTaxonomy> categories = await _mediator.QueryAsync<IReadOnlyList<CategoryTaxonomy>>(
            new GetAllCategoriesQuery(), ct);
        await SendOkAsync(categories, ct);
    }

    internal sealed class Request { }
}

// ── Spend Analytics Queries (BR-SA-02..06) ───────────────────────────

internal sealed class GetSpendByCategoryEndpoint : Endpoint<GetSpendByCategoryEndpoint.Request, IReadOnlyList<SpendCubeEntry>>
{
    private readonly IMediator _mediator;

    public GetSpendByCategoryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/spend/by-category");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get spend aggregated by category (BR-SA-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<SpendCubeEntry> result = await _mediator.QueryAsync<IReadOnlyList<SpendCubeEntry>>(
            new GetSpendByCategoryQuery(req.FromPeriod, req.ToPeriod, req.CategoryId), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
        public Guid? CategoryId { get; set; }
    }
}

internal sealed class GetSpendByVendorEndpoint : Endpoint<GetSpendByVendorEndpoint.Request, IReadOnlyList<SpendCubeEntry>>
{
    private readonly IMediator _mediator;

    public GetSpendByVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/spend/by-vendor/{vendorId}");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get spend for a specific vendor (BR-SA-02)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<SpendCubeEntry> result = await _mediator.QueryAsync<IReadOnlyList<SpendCubeEntry>>(
            new GetSpendByVendorQuery(req.VendorId, req.FromPeriod, req.ToPeriod), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
    }
}

internal sealed class GetPriceVarianceEndpoint : Endpoint<GetPriceVarianceEndpoint.Request, IReadOnlyList<PriceVarianceDto>>
{
    private readonly IMediator _mediator;

    public GetPriceVarianceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/price-variance");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get price variance analysis (BR-SA-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<PriceVarianceDto> result = await _mediator.QueryAsync<IReadOnlyList<PriceVarianceDto>>(
            new GetPriceVarianceQuery(req.FromPeriod, req.ToPeriod, req.VendorId, req.CategoryId), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
        public Guid? VendorId { get; set; }
        public Guid? CategoryId { get; set; }
    }
}

internal sealed class GetSavingsTrackerEndpoint : Endpoint<GetSavingsTrackerEndpoint.Request, IReadOnlyList<SavingsEntryDto>>
{
    private readonly IMediator _mediator;

    public GetSavingsTrackerEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/savings");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get savings tracker (BR-SA-04)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<SavingsEntryDto> result = await _mediator.QueryAsync<IReadOnlyList<SavingsEntryDto>>(
            new GetSavingsTrackerQuery(req.FromPeriod, req.ToPeriod, req.TopN), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
        public int TopN { get; set; } = 10;
    }
}

internal sealed class GetTailSpendEndpoint : Endpoint<GetTailSpendEndpoint.Request, TailSpendDto>
{
    private readonly IMediator _mediator;

    public GetTailSpendEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/tail-spend");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get tail-spend analysis (BR-SA-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        TailSpendDto result = await _mediator.QueryAsync<TailSpendDto>(
            new GetTailSpendQuery(req.FromPeriod, req.ToPeriod), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
    }
}

internal sealed class GetSingleSourceRiskEndpoint : Endpoint<GetSingleSourceRiskEndpoint.Request, IReadOnlyList<SingleSourceRiskDto>>
{
    private readonly IMediator _mediator;

    public GetSingleSourceRiskEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/analytics/single-source-risk");
        Tag(SpendAnalysisTags.SpendAnalytics);
        Summary("Get single-source risk exposure (BR-SA-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        IReadOnlyList<SingleSourceRiskDto> result = await _mediator.QueryAsync<IReadOnlyList<SingleSourceRiskDto>>(
            new GetSingleSourceRiskQuery(req.FromPeriod, req.ToPeriod, req.ThresholdPercent), ct);
        await SendOkAsync(result, ct);
    }

    internal sealed class Request
    {
        public DateOnly FromPeriod { get; set; }
        public DateOnly ToPeriod { get; set; }
        public decimal ThresholdPercent { get; set; } = 80m;
    }
}
