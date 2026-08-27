using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

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
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/partners/{result.Value}", ct);
    }

    internal sealed class CreatePartnerRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }
}
