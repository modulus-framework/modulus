using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.WorkflowEngine.Application.Commands;
using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Modules.WorkflowEngine.Application.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Presentation.Workflows;

internal sealed class CreateWorkflowDefinitionEndpoint : Endpoint<CreateWorkflowDefinitionEndpoint.CreateDefinitionRequest, WorkflowDefinitionResponse>
{
    private readonly IMediator _mediator;

    public CreateWorkflowDefinitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/workflow/definitions");
        Tag(Tags.WorkflowDefinitions);
        Summary("Create a new workflow definition draft");
    }

    public override async Task HandleAsync(CreateDefinitionRequest req, CancellationToken ct)
    {
        var command = new CreateWorkflowDefinitionCommand(
            req.Key, req.Name, req.DocumentType, req.TriggerEvent,
            req.StepsJson, req.ContextSchemaJson, req.OnRejectJson, req.OnTimeoutAction);

        Result<WorkflowDefinitionResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/workflow/definitions/{result.Value.Id}", ct);
    }

    internal sealed class CreateDefinitionRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string TriggerEvent { get; set; } = string.Empty;
        public string StepsJson { get; set; } = string.Empty;
        public string? ContextSchemaJson { get; set; }
        public string? OnRejectJson { get; set; }
        public string? OnTimeoutAction { get; set; }
    }
}

internal sealed class PublishWorkflowDefinitionEndpoint : Endpoint<PublishWorkflowDefinitionEndpoint.PublishRequest, WorkflowDefinitionResponse>
{
    private readonly IMediator _mediator;

    public PublishWorkflowDefinitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/workflow/definitions/{Id}/publish");
        Tag(Tags.WorkflowDefinitions);
        Summary("Publish a workflow definition");
    }

    public override async Task HandleAsync(PublishRequest req, CancellationToken ct)
    {
        var command = new PublishWorkflowDefinitionCommand(req.Id, req.PublishedBy);
        Result<WorkflowDefinitionResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class PublishRequest
    {
        public Guid Id { get; set; }
        public string PublishedBy { get; set; } = string.Empty;
    }
}

internal sealed class GetWorkflowDefinitionEndpoint : Endpoint<GetWorkflowDefinitionEndpoint.GetByIdRequest, WorkflowDefinitionResponse>
{
    private readonly IMediator _mediator;

    public GetWorkflowDefinitionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/workflow/definitions/{Id}");
        Tag(Tags.WorkflowDefinitions);
        Summary("Get a workflow definition by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        Result<WorkflowDefinitionResponse> result = await _mediator.QueryAsync(new GetWorkflowDefinitionByIdQuery(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetByIdRequest
    {
        public Guid Id { get; set; }
    }
}
