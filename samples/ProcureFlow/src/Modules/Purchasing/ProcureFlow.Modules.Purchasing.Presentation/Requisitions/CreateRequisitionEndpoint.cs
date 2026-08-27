using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class CreateRequisitionEndpoint : Endpoint<CreateRequisitionEndpoint.CreateRequisitionRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateRequisitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/purchase-requisitions");
        Tag("Purchasing");
        Summary("Create a new purchase requisition");
    }

    public override async Task HandleAsync(CreateRequisitionRequest req, CancellationToken ct)
    {
        var command = new CreatePurchaseRequisitionCommand(req.RequisitionNumber, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/purchase-requisitions/{result.Value}", ct);
    }

    internal sealed class CreateRequisitionRequest
    {
        public string RequisitionNumber { get; set; } = string.Empty;
        public Guid OrgUnitId { get; set; }
    }
}
