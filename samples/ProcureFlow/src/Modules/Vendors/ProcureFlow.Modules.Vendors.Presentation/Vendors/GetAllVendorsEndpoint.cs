using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Application.Vendors.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class GetAllVendorsEndpoint : Endpoint<GetAllVendorsEndpoint.EmptyRequest, IReadOnlyList<VendorResponse>>
{
    private readonly IMediator _mediator;

    public GetAllVendorsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/vendors");
        Tag(Tags.Vendors);
        Summary("List all vendors");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<VendorResponse>> result = await _mediator.QueryAsync(new GetAllVendorsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class EmptyRequest
    {
    }
}
