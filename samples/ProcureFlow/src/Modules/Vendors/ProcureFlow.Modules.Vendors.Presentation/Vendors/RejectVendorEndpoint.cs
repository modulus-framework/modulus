using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class RejectVendorEndpoint : Endpoint<RejectVendorEndpoint.Request, VendorStatusResponse>
{
    private readonly IMediator _mediator;

    public RejectVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/reject");
        Tag(Tags.Vendors);
        Summary("Reject a vendor");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorStatusResponse> result = await _mediator.SendAsync(new RejectVendorCommand(req.VendorId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
