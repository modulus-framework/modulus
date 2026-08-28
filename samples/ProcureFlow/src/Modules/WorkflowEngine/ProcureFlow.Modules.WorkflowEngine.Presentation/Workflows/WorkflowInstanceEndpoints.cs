using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.WorkflowEngine.Application.Commands;
using ProcureFlow.Modules.WorkflowEngine.Application.Dtos;
using ProcureFlow.Modules.WorkflowEngine.Application.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Presentation.Workflows;

internal sealed class StartWorkflowInstanceEndpoint : Endpoint<StartWorkflowInstanceEndpoint.StartInstanceRequest, WorkflowInstanceResponse>
{
    private readonly IMediator _mediator;

    public StartWorkflowInstanceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/workflow/instances");
        Tag(Tags.WorkflowInstances);
        Summary("Start a new workflow instance");
    }

    public override async Task HandleAsync(StartInstanceRequest req, CancellationToken ct)
    {
        var command = new StartWorkflowInstanceCommand(
            req.DefinitionKey, req.DocumentType, req.DocumentId, req.ContextJson);

        Result<WorkflowInstanceResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/workflow/instances/{result.Value.Id}", ct);
    }

    internal sealed class StartInstanceRequest
    {
        public string DefinitionKey { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public Guid DocumentId { get; set; }
        public string? ContextJson { get; set; }
    }
}

internal sealed class GetWorkflowInstanceEndpoint : Endpoint<GetWorkflowInstanceEndpoint.GetByIdRequest, WorkflowInstanceResponse>
{
    private readonly IMediator _mediator;

    public GetWorkflowInstanceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/workflow/instances/{Id}");
        Tag(Tags.WorkflowInstances);
        Summary("Get a workflow instance by ID");
    }

    public override async Task HandleAsync(GetByIdRequest req, CancellationToken ct)
    {
        Result<WorkflowInstanceResponse> result = await _mediator.QueryAsync(new GetWorkflowInstanceByIdQuery(req.Id), ct);

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

internal sealed class CompleteTaskEndpoint : Endpoint<CompleteTaskEndpoint.CompleteTaskRequest, WorkflowInstanceResponse>
{
    private readonly IMediator _mediator;

    public CompleteTaskEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/workflow/tasks/{TaskId}/complete");
        Tag(Tags.WorkflowTasks);
        Summary("Complete a workflow task (approve/reject/return)");
    }

    public override async Task HandleAsync(CompleteTaskRequest req, CancellationToken ct)
    {
        var command = new CompleteTaskCommand(req.TaskId, req.Decision, req.Reason);
        Result<WorkflowInstanceResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class CompleteTaskRequest
    {
        public Guid TaskId { get; set; }
        public int Decision { get; set; }
        public string? Reason { get; set; }
    }
}

internal sealed class GetMyOpenTasksEndpoint : Endpoint<GetMyOpenTasksEndpoint.MyTasksRequest, IReadOnlyList<WorkflowTaskResponse>>
{
    private readonly IMediator _mediator;

    public GetMyOpenTasksEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/workflow/tasks/my-open");
        Tag(Tags.WorkflowTasks);
        Summary("Get open tasks assigned to the current user");
    }

    public override async Task HandleAsync(MyTasksRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<WorkflowTaskResponse>> result = await _mediator.QueryAsync(new GetMyOpenTasksQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class MyTasksRequest
    {
    }
}
