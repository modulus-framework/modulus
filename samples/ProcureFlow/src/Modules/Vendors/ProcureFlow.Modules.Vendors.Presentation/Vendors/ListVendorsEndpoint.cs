using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Application.Vendors.Queries;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class ListVendorsEndpoint : Endpoint<ListVendorsEndpoint.Request, IReadOnlyList<VendorResponse>>
{
    private readonly IMediator _mediator;

    public ListVendorsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/vendors/list");
        Tag(Tags.Vendors);
        Summary("List vendors with optional filters (status, country, type, search)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var query = new ListVendorsQuery(
            Status: req.Status,
            Country: req.Country,
            VendorType: req.VendorType,
            SearchTerm: req.SearchTerm);

        Result<IReadOnlyList<VendorResponse>> result = await _mediator.QueryAsync(query, ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public VendorStatus? Status { get; set; }
        public string? Country { get; set; }
        public VendorType? VendorType { get; set; }
        public string? SearchTerm { get; set; }
    }
}
