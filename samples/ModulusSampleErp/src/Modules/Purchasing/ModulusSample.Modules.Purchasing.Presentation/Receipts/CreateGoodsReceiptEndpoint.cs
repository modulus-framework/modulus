using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class CreateGoodsReceiptEndpoint : Endpoint<CreateGoodsReceiptEndpoint.CreateReceiptRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateGoodsReceiptEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/goods-receipts");
        Tag("Purchasing");
        Summary("Create a new goods receipt");
    }

    public override async Task HandleAsync(CreateReceiptRequest req, CancellationToken ct)
    {
        var command = new CreateGoodsReceiptCommand(req.ReceiptNumber, req.PurchaseOrderId, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/goods-receipts/{result.Value}", ct);
    }

    public sealed record CreateReceiptRequest(string ReceiptNumber, Guid PurchaseOrderId, Guid OrgUnitId);
}
