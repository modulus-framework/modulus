using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class ConfirmPaymentEndpoint : Endpoint<ConfirmPaymentEndpoint.ConfirmPaymentRequest>
{
    private readonly IMediator _mediator;

    public ConfirmPaymentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/payments/{id:guid}/confirm");
        Tag("Billing");
        Summary("Confirm a payment");
    }

    public override async Task HandleAsync(ConfirmPaymentRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(
            new ConfirmPaymentCommand(req.Id, req.ReferenceNumber), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class ConfirmPaymentRequest
    {
        public Guid Id { get; set; }
        public string? ReferenceNumber { get; set; }
    }
}
