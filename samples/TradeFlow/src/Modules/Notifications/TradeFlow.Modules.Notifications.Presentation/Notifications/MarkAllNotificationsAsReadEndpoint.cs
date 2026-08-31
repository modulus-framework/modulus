using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Notifications;

internal sealed class MarkAllNotificationsAsReadEndpoint : EndpointWithoutRequest<MarkAllReadResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public MarkAllNotificationsAsReadEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Patch("/notifications/read-all");
        Tag(Tags.Notifications);
        Summary("Mark all of the current user's notifications as read");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var command = new MarkAllNotificationsAsReadCommand(_currentTenant.TenantId ?? Guid.Empty);
        Result<MarkAllReadResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
