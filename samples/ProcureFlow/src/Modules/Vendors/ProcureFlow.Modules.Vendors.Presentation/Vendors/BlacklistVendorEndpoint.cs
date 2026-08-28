using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class BlacklistVendorEndpoint : Endpoint<BlacklistVendorEndpoint.Request, VendorStatusResponse>
{
    private readonly IMediator _mediator;

    public BlacklistVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/blacklist");
        Tag(Tags.Vendors);
        Summary("Blacklist a vendor (BR-VEN-08)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorStatusResponse> result = await _mediator.SendAsync(new BlacklistVendorCommand(req.VendorId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
