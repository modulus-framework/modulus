using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Commands;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Notifications;

internal sealed class
    CreateNotificationEndpoint : Endpoint<CreateNotificationEndpoint.CreateNotificationRequest, NotificationResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public CreateNotificationEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Post("/notifications");
        Tag(Tags.Notifications);
        Summary("Create a new notification for a user");
    }

    public override async Task HandleAsync(CreateNotificationRequest req, CancellationToken ct)
    {
        var command = new CreateNotificationCommand(
            req.RecipientUserId,
            req.Title,
            req.Message,
            req.Type,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<NotificationResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/notifications/{result.Value.NotificationId}", ct);
    }

    internal sealed class CreateNotificationRequest
    {
        public Guid RecipientUserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
    }
}
