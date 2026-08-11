using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class SubmitRequisitionEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public SubmitRequisitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/purchase-requisitions/{id:guid}/submit");
        Tag("Purchasing");
        Summary("Submit a purchase requisition");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new SubmitPurchaseRequisitionCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
