using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.WorkflowEngine.Domain.Errors;

public static class WorkflowErrors
{
    public static Error DefinitionNotFound(Guid id) =>
        Error.NotFound("Workflow.Definition.NotFound", $"Workflow definition {id} not found");

    public static Error DefinitionNotPublished(string key) =>
        Error.NotFound("Workflow.Definition.NotPublished", $"No published workflow definition for key '{key}'");

    public static Error InstanceNotFound(Guid id) =>
        Error.NotFound("Workflow.Instance.NotFound", $"Workflow instance {id} not found");

    public static Error TaskNotFound(Guid id) =>
        Error.NotFound("Workflow.Task.NotFound", $"Workflow task {id} not found");

    public static Error TaskNotOpen() =>
        Error.BusinessRule("Workflow.Task.NotOpen", "Task is not in Open status");

    public static Error InstanceNotRunning() =>
        Error.BusinessRule("Workflow.Instance.NotRunning", "Workflow instance is not in Running status");

    public static Error DefinitionAlreadyPublished() =>
        Error.BusinessRule("Workflow.Definition.AlreadyPublished", "Definition is already published");

    public static Error SelfApprovalForbidden() =>
        Error.Forbidden("Workflow.SelfApproval", "Self-approval is not allowed (SoD violation)");
}
