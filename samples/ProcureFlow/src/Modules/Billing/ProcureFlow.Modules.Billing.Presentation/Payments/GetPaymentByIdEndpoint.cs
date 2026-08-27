using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;
using ModulusSample.Modules.Billing.Application.Payments.Queries;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class GetPaymentByIdEndpoint : Endpoint<GetPaymentByIdEndpoint.GetPaymentByIdRequest, PaymentDto>
{
    private readonly IMediator _mediator;

    public GetPaymentByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/payments/{id:guid}");
        Tag("Billing");
        Summary("Get payment details");
    }

    public override async Task HandleAsync(GetPaymentByIdRequest req, CancellationToken ct)
    {
        PaymentDto? payment = await _mediator.QueryAsync(new GetPaymentByIdQuery(req.Id), ct);

        if (payment is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(payment, ct);
    }

    internal sealed class GetPaymentByIdRequest
    {
        public Guid Id { get; set; }
    }
}
