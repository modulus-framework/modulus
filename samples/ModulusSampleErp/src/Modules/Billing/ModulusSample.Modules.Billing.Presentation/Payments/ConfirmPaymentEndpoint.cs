using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class ConfirmPaymentEndpoint : Endpoint<ConfirmPaymentCommand>
{
    private readonly IMediator _mediator;

    public ConfirmPaymentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/payments/{id:guid}/confirm");
        Tag("Billing");
        Summary("Confirm a payment");
    }

    public override async Task HandleAsync(ConfirmPaymentCommand req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = req with { PaymentId = id };
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
