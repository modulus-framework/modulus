using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class SubmitRequisitionEndpoint : Endpoint<SubmitRequisitionEndpoint.SubmitRequisitionRequest>
{
    private readonly IMediator _mediator;

    public SubmitRequisitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/purchase-requisitions/{id:guid}/submit");
        Tag("Purchasing");
        Summary("Submit a purchase requisition");
    }

    public override async Task HandleAsync(SubmitRequisitionRequest req, CancellationToken ct)
    {
        var command = new SubmitPurchaseRequisitionCommand(req.Id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class SubmitRequisitionRequest
    {
        public Guid Id { get; set; }
    }
}
