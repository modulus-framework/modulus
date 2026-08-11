using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Presentation.Partners;

internal sealed class CreatePartnerEndpoint : Endpoint<CreatePartnerEndpoint.CreatePartnerRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreatePartnerEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/partners");
        Tag("Partners");
        Summary("Create a new partner (customer or supplier)");
    }

    public override async Task HandleAsync(CreatePartnerRequest req, CancellationToken ct)
    {
        var command = new CreatePartnerCommand(req.Name, req.Type, req.Email, req.Phone, req.Address);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/partners/{result.Value}", ct);
    }

    public sealed record CreatePartnerRequest(string Name, string Type, string Email, string Phone, string Address);
}
