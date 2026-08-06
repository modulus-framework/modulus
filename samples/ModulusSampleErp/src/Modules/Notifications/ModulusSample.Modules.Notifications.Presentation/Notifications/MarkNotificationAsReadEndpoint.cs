using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Notifications.Application.Notifications.Commands;
using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Presentation.Notifications;

internal sealed class MarkNotificationAsReadEndpoint : Endpoint<MarkNotificationAsReadEndpoint.MarkNotificationAsReadRequest, NotificationResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public MarkNotificationAsReadEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Patch("/notifications/{notificationId}/read");
        Tag(Tags.Notifications);
        Summary("Mark a notification as read");
    }

    public override async Task HandleAsync(MarkNotificationAsReadRequest req, CancellationToken ct)
    {
        var command = new MarkNotificationAsReadCommand(
            req.NotificationId,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<NotificationResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class MarkNotificationAsReadRequest
    {
        public Guid NotificationId { get; set; }
    }
}