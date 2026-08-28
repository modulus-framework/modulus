using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class AddVendorScorecardEndpoint : Endpoint<AddVendorScorecardEndpoint.Request, VendorScorecardResponse>
{
    private readonly IMediator _mediator;

    public AddVendorScorecardEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/scorecards");
        Tag(Tags.Vendors);
        Summary("Record a vendor scorecard (BR-VEN-07: OTD 35%, Quality 30%, Price 15%, Responsiveness 10%, Compliance 10%)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorScorecardResponse> result = await _mediator.SendAsync(new AddVendorScorecardCommand(
            req.VendorId, req.Period, req.OnTimeDeliveryScore, req.QualityScore,
            req.PriceCompetitivenessScore, req.ResponsivenessScore, req.ComplianceScore), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public DateOnly Period { get; set; }
        public decimal OnTimeDeliveryScore { get; set; }
        public decimal QualityScore { get; set; }
        public decimal PriceCompetitivenessScore { get; set; }
        public decimal ResponsivenessScore { get; set; }
        public decimal ComplianceScore { get; set; }
    }
}
