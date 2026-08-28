using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class QualifyVendorEndpoint : Endpoint<QualifyVendorEndpoint.Request, VendorStatusResponse>
{
    private readonly IMediator _mediator;

    public QualifyVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/qualify");
        Tag(Tags.Vendors);
        Summary("Qualify a vendor for a category (BR-VEN-05)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorStatusResponse> result = await _mediator.SendAsync(new QualifyVendorCommand(
            req.VendorId, req.Category, req.CertificateNumber, req.ValidFrom, req.ValidTo), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string CertificateNumber { get; set; } = string.Empty;
        public DateOnly ValidFrom { get; set; }
        public DateOnly ValidTo { get; set; }
    }
}
