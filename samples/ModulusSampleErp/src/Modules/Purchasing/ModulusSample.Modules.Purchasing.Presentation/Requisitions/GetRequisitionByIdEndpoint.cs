using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class GetRequisitionByIdEndpoint : Endpoint<RequisitionDto>
{
    private readonly IMediator _mediator;

    public GetRequisitionByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-requisitions/{id:guid}");
        Tag("Purchasing");
        Summary("Get purchase requisition details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<RequisitionDto> result = await _mediator.QueryAsync(new GetRequisitionByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
