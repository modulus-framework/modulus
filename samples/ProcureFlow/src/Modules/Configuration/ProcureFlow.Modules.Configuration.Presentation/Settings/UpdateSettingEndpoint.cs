using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Settings;

internal sealed class UpdateSettingEndpoint : Endpoint<UpdateSettingEndpoint.UpdateSettingRequest, UpdateSettingResponse>
{
    private readonly IMediator _mediator;

    public UpdateSettingEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Patch("/settings/{settingId}");
        Tag(Tags.Settings);
        Summary("Update a setting's metadata");
    }

    public override async Task HandleAsync(UpdateSettingRequest req, CancellationToken ct)
    {
        var command = new UpdateSettingCommand(req.SettingId, req.Category, req.Description, req.IsPublic);
        Result<UpdateSettingResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateSettingRequest
    {
        public Guid SettingId { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public bool? IsPublic { get; set; }
    }
}
