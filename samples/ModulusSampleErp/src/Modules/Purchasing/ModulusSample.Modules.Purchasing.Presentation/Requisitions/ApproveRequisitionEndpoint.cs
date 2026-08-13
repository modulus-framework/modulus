using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class ApproveRequisitionEndpoint : Endpoint<ApproveRequisitionEndpoint.ApproveRequisitionRequest>
{
    private readonly IMediator _mediator;

    public ApproveRequisitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/purchase-requisitions/{id:guid}/approve");
        Tag("Purchasing");
        Summary("Approve a purchase requisition");
    }

    public override async Task HandleAsync(ApproveRequisitionRequest req, CancellationToken ct)
    {
        var command = new ApprovePurchaseRequisitionCommand(req.Id, req.ApproverId);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class ApproveRequisitionRequest
    {
        public Guid Id { get; set; }
        public Guid ApproverId { get; set; }
    }
}
