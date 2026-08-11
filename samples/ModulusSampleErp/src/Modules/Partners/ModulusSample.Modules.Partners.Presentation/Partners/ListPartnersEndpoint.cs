using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;
using ModulusSample.Modules.Partners.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Presentation.Partners;

internal sealed class ListPartnersEndpoint : Endpoint<PagedResult<PartnerDto>>
{
    private readonly IMediator _mediator;

    public ListPartnersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/partners");
        Tag("Partners");
        Summary("List all partners");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<PartnerDto>> result = await _mediator.QueryAsync(new ListPartnersQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
