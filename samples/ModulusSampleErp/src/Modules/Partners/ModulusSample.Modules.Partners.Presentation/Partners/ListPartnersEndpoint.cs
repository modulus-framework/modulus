using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Dtos;
using ModulusSample.Modules.Partners.Application.Partners.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Presentation.Partners;

internal sealed class ListPartnersEndpoint : Endpoint<ListPartnersEndpoint.ListPartnersRequest, PagedResult<PartnerDto>>
{
    private readonly IMediator _mediator;

    public ListPartnersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/partners");
        Tag("Partners");
        Summary("List all partners");
    }

    public override async Task HandleAsync(ListPartnersRequest req, CancellationToken ct)
    {
        PagedResult<PartnerDto> result = await _mediator.QueryAsync(new ListPartnersQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListPartnersRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
