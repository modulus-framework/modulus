using Meetup.Modules.Payments.Application.Commands.PayMeetingFee;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Presentation.Endpoints;

public sealed class PayMeetingFeeRequest
{
    public string PayerLogin { get; set; } = string.Empty;
    public Guid MeetingId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed class PayMeetingFeeEndpoint(IMediator mediator) : Endpoint<PayMeetingFeeRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/payments/meeting-fees");
        Permissions(PaymentsPermissions.PayMeetingFee);
        Summary("Pays a meeting's event fee");
    }

    public override async Task HandleAsync(PayMeetingFeeRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(
            new PayMeetingFeeCommand(req.PayerLogin, req.MeetingId, req.Amount, req.Currency), ct);
        await SendCreatedAsync(id, $"/api/payments/meeting-fees/{id}", ct);
    }
}
