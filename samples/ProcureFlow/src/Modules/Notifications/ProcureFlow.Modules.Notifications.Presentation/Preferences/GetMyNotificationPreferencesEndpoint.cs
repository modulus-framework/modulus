using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Application.Notifications.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Preferences;

internal sealed class GetMyNotificationPreferencesEndpoint(
    IMediator mediator) : Endpoint<GetMyNotificationPreferencesEndpoint.GetMyPrefsRequest, IReadOnlyList<NotificationPreferenceResponse>>
{
    public override void Configure()
    {
        Get("/notifications/preferences/me");
        Tag(Tags.Notifications);
        Summary("Get current user's notification preferences");
    }

    public override async Task HandleAsync(GetMyPrefsRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<NotificationPreferenceResponse>> result = await mediator.QueryAsync(
            new GetMyNotificationPreferencesQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetMyPrefsRequest { }
}
