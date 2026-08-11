using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;
using ModulusSample.Modules.Partners.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Presentation.Partners;

internal sealed class GetPartnerByIdEndpoint : Endpoint<PartnerDto>
{
    private readonly IMediator _mediator;

    public GetPartnerByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/partners/{id:guid}");
        Tag("Partners");
        Summary("Get partner details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<PartnerDto> result = await _mediator.QueryAsync(new GetPartnerByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
