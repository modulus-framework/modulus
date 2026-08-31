using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class SubmitVendorEndpoint : Endpoint<SubmitVendorEndpoint.Request, VendorStatusResponse>
{
    private readonly IMediator _mediator;

    public SubmitVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/submit");
        Tag(Tags.Vendors);
        Summary("Submit a vendor for review");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorStatusResponse> result = await _mediator.SendAsync(new SubmitVendorCommand(req.VendorId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
    }
}
