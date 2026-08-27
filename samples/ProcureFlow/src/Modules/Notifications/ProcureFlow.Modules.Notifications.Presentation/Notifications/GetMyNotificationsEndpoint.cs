using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Application.Notifications.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Notifications;

internal sealed class GetMyNotificationsEndpoint : Endpoint<GetMyNotificationsEndpoint.GetMyNotificationsRequest, PagedResult<NotificationResponse>>
{
    private readonly IMediator _mediator;

    public GetMyNotificationsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/notifications/my");
        Tag(Tags.Notifications);
        Summary("Get the current user's notifications with optional filtering");
    }

    public override async Task HandleAsync(GetMyNotificationsRequest req, CancellationToken ct)
    {
        var query = new GetMyNotificationsQuery(req.IsRead, req.PageNumber, req.PageSize);
        Result<PagedResult<NotificationResponse>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetMyNotificationsRequest
    {
        public bool? IsRead { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
