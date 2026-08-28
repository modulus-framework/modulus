using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Modules.Notifications.Application.Notifications.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Templates;

internal sealed class GetNotificationTemplatesEndpoint(
    IMediator mediator) : Endpoint<GetNotificationTemplatesEndpoint.GetTemplatesRequest, IReadOnlyList<NotificationTemplateResponse>>
{
    public override void Configure()
    {
        Get("/notifications/templates");
        Tag(Tags.Notifications);
        Summary("List all notification templates for the current tenant");
    }

    public override async Task HandleAsync(GetTemplatesRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<NotificationTemplateResponse>> result = await mediator.QueryAsync(
            new GetNotificationTemplatesQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetTemplatesRequest { }
}
