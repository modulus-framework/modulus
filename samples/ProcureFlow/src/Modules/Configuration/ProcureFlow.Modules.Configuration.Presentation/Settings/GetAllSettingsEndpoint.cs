using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Queries;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Presentation.Settings;

internal sealed class GetAllSettingsEndpoint : Endpoint<GetAllSettingsEndpoint.GetAllSettingsRequest, PagedResult<SettingResponse>>
{
    private readonly IMediator _mediator;

    public GetAllSettingsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/settings");
        Tag(Tags.Settings);
        Summary("Get all settings with optional filtering");
    }

    public override async Task HandleAsync(GetAllSettingsRequest req, CancellationToken ct)
    {
        var query = new GetAllSettingsQuery(req.Category, req.IsPublic, req.PageNumber, req.PageSize);
        Result<PagedResult<SettingResponse>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetAllSettingsRequest
    {
        public string? Category { get; set; }
        public bool? IsPublic { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
