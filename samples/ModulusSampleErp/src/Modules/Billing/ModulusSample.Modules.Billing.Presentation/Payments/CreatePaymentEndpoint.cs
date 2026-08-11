using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class CreatePaymentEndpoint : Endpoint<CreatePaymentCommand, Guid>
{
    private readonly IMediator _mediator;

    public CreatePaymentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/payments");
        Tag("Billing");
        Summary("Create a new payment");
    }

    public override async Task HandleAsync(CreatePaymentCommand req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(req, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/payments/{result.Value}", ct);
    }
}
