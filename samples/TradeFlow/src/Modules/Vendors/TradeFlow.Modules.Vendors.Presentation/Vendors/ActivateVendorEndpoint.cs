using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class ActivateVendorEndpoint : Endpoint<ActivateVendorEndpoint.Request, VendorStatusResponse>
{
    private readonly IMediator _mediator;

    public ActivateVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/activate");
        Tag(Tags.Vendors);
        Summary("Activate a qualified vendor");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorStatusResponse> result = await _mediator.SendAsync(new ActivateVendorCommand(req.VendorId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
    }
}
