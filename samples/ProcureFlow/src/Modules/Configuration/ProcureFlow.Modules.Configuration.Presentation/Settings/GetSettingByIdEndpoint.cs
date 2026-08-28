using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Queries;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Settings;

internal sealed class GetSettingByIdEndpoint : Endpoint<GetSettingByIdEndpoint.GetByIdRequest, SettingResponse>
{
    private readonly IMediator _mediator;

    public GetSettingByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/settings/{settingId}");
        Tag(Tags.Settings);
        Summary("Get a setting by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        var query = new GetSettingByIdQuery(req.SettingId);
        Result<SettingResponse> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByIdRequest
    {
        public Guid SettingId { get; set; }
    }
}
