using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Modules.Notifications.Application.Notifications.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Presentation.Notifications;

internal sealed class GetNotificationByIdEndpoint : Endpoint<GetNotificationByIdEndpoint.GetNotificationByIdRequest, NotificationResponse>
{
    private readonly IMediator _mediator;

    public GetNotificationByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/notifications/{notificationId}");
        Tag(Tags.Notifications);
        Summary("Get a notification by ID")RequireAuthorization(); ;
    }

    public override async Task HandleAsync(GetNotificationByIdRequest req, CancellationToken ct)
    {
        Result<NotificationResponse> result = await _mediator.QueryAsync(new GetNotificationByIdQuery(req.NotificationId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetNotificationByIdRequest
    {
        public Guid NotificationId { get; set; }
    }
}
