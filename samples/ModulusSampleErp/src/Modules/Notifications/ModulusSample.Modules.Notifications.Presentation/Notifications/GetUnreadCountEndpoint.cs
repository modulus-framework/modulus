using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Modules.Notifications.Application.Notifications.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Presentation.Notifications;

internal sealed class GetUnreadCountEndpoint : EndpointWithoutRequest<UnreadCountResponse>
{
    private readonly IMediator _mediator;

    public GetUnreadCountEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/notifications/unread-count");
        Tag(Tags.Notifications);
        Summary("Get the current user's unread notification count");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<UnreadCountResponse> result = await _mediator.QueryAsync(new GetUnreadCountQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}