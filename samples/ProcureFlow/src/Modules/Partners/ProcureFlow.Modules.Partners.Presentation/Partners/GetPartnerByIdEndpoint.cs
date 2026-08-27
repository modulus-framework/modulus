using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Dtos;
using ModulusSample.Modules.Partners.Application.Partners.Queries;

namespace ModulusSample.Modules.Partners.Presentation.Partners;

internal sealed class GetPartnerByIdEndpoint : Endpoint<GetPartnerByIdEndpoint.GetPartnerByIdRequest, PartnerDto>
{
    private readonly IMediator _mediator;

    public GetPartnerByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/partners/{id:guid}");
        Tag("Partners");
        Summary("Get partner details");
    }

    public override async Task HandleAsync(GetPartnerByIdRequest req, CancellationToken ct)
    {
        PartnerDto? result = await _mediator.QueryAsync(new GetPartnerByIdQuery(req.Id), ct);

        if (result is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result, ct);
    }

    internal sealed class GetPartnerByIdRequest
    {
        public Guid Id { get; set; }
    }
}
