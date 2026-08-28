using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Commands;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Events;

internal sealed class ProcessNotificationEventEndpoint(
    IMediator mediator) : Endpoint<ProcessNotificationEventEndpoint.ProcessEventRequest, ProcessEventResponse>
{
    public override void Configure()
    {
        Post("/notifications/events");
        Tag(Tags.Notifications);
        Summary("Process a business event through the notification engine");
    }

    public override async Task HandleAsync(ProcessEventRequest req, CancellationToken ct)
    {
        var command = new ProcessNotificationEventCommand(
            req.EventKey, req.PayloadJson, req.TriggerUserId);

        Result<ProcessEventResponse> result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ProcessEventRequest
    {
        public string EventKey { get; set; } = string.Empty;
        public string? PayloadJson { get; set; }
        public Guid? TriggerUserId { get; set; }
    }
}
