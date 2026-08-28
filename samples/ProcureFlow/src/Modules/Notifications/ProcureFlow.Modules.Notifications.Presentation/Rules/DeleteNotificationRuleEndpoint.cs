using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Notifications.Application.Notifications.Commands;
using ProcureFlow.Modules.Notifications.Application.Notifications.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Presentation.Rules;

internal sealed class DeleteNotificationRuleEndpoint(
    IMediator mediator) : Endpoint<DeleteNotificationRuleEndpoint.DeleteRuleRequest>
{
    public override void Configure()
    {
        Delete("/notifications/rules/{RuleId}");
        Tag(Tags.Notifications);
        Summary("Delete a notification rule");
    }

    public override async Task HandleAsync(DeleteRuleRequest req, CancellationToken ct)
    {
        var command = new DeleteNotificationRuleCommand(req.RuleId);
        Result result = await mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteRuleRequest
    {
        public Guid RuleId { get; set; }
    }
}
