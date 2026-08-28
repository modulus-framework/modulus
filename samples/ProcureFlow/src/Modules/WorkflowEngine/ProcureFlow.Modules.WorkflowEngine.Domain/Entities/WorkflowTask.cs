using ProcureFlow.Modules.WorkflowEngine.Domain.Enums;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Domain.Entities;

/// <summary>
/// A single task within a workflow instance (approval step, human task, etc.).
/// </summary>
public sealed class WorkflowTask
{
    private WorkflowTask() { }

    public WorkflowTask(
        Guid id, Guid instanceId, string stepId, string stepType,
        Guid? assigneeUserId, string? assigneeRole, string? assigneeResolutionJson,
        DateTime? dueAtUtc)
    {
        Id = id;
        InstanceId = instanceId;
        StepId = stepId;
        StepType = stepType;
        AssigneeUserId = assigneeUserId;
        AssigneeRole = assigneeRole;
        AssigneeResolutionJson = assigneeResolutionJson;
        Status = Domain.Enums.TaskStatus.Open;
        DueAtUtc = dueAtUtc;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid InstanceId { get; private set; }
    public string StepId { get; private set; } = null!;
    public string StepType { get; private set; } = null!;
    public Guid? AssigneeUserId { get; private set; }
    public string? AssigneeRole { get; private set; }
    public string? AssigneeResolutionJson { get; private set; }
    public Domain.Enums.TaskStatus Status { get; private set; }
    public TaskDecision? Decision { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ActedByUserId { get; private set; }
    public DateTime? ActedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Result Claim(Guid userId)
    {
        if (Status != Domain.Enums.TaskStatus.Open)
            return Result.Failure(Error.BusinessRule("WorkflowTask.AlreadyClaimed", "Task is not open for claiming"));

        Status = Domain.Enums.TaskStatus.Done;
        ActedByUserId = userId;
        ActedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Complete(TaskDecision decision, Guid userId, string? reason = null)
    {
        if (Status != Domain.Enums.TaskStatus.Open)
            return Result.Failure(Error.BusinessRule("WorkflowTask.NotOpen", "Task is not in Open status"));

        if (decision == TaskDecision.Return && string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("WorkflowTask.ReturnRequiresReason", "Return decision requires a reason"));

        Status = Domain.Enums.TaskStatus.Done;
        Decision = decision;
        Reason = reason;
        ActedByUserId = userId;
        ActedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reassign(Guid newAssigneeUserId)
    {
        if (Status != Domain.Enums.TaskStatus.Open)
            return Result.Failure(Error.BusinessRule("WorkflowTask.NotOpen", "Only open tasks can be reassigned"));

        Status = Domain.Enums.TaskStatus.Reassigned;
        AssigneeUserId = newAssigneeUserId;
        return Result.Success();
    }

    public void Expire()
    {
        if (Status == Domain.Enums.TaskStatus.Open)
        {
            Status = Domain.Enums.TaskStatus.Expired;
            ActedAtUtc = DateTime.UtcNow;
        }
    }
}
