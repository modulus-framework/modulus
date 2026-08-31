using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Application.Vendors.Queries;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class GetVendorByIdEndpoint : Endpoint<GetVendorByIdEndpoint.GetByIdRequest, VendorDetailResponse>
{
    private readonly IMediator _mediator;

    public GetVendorByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/vendors/{vendorId}");
        Tag(Tags.Vendors);
        Summary("Get a vendor by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        Result<VendorDetailResponse> result = await _mediator.QueryAsync(new GetVendorByIdQuery(req.VendorId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByIdRequest
    {
        public Guid VendorId { get; set; }
    }
}
