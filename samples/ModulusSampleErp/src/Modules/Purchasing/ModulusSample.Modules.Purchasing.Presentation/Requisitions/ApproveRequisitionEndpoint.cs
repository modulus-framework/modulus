using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Shared.Domain;

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
        var id = Route<Guid>("id");
        var command = new ApprovePurchaseRequisitionCommand(id, req.ApproverId);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }

    public sealed record ApproveRequisitionRequest(Guid ApproverId);
}
