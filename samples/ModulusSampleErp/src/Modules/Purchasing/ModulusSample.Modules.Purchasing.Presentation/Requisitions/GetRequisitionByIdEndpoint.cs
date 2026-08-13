using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class GetRequisitionByIdEndpoint : Endpoint<GetRequisitionByIdEndpoint.GetRequisitionByIdRequest, PurchaseRequisitionDto>
{
    private readonly IMediator _mediator;

    public GetRequisitionByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-requisitions/{id:guid}");
        Tag("Purchasing");
        Summary("Get purchase requisition details");
    }

    public override async Task HandleAsync(GetRequisitionByIdRequest req, CancellationToken ct)
    {
        Result<PurchaseRequisitionDto?> result = await _mediator.QueryAsync(new GetRequisitionByIdQuery(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        if (result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetRequisitionByIdRequest
    {
        public Guid Id { get; set; }
    }
}
