namespace TradeFlow.Modules.WorkflowEngine.Presentation;

public static class Permissions
{
    public const string WorkflowEngineGroup = "WorkflowEngine";

    public static class Definitions
    {
        public const string Create = "workflow.definitions.create";
        public const string Read = "workflow.definitions.read";
        public const string Publish = "workflow.definitions.publish";
        public const string Retire = "workflow.definitions.retire";
    }

    public static class Instances
    {
        public const string Start = "workflow.instances.start";
        public const string Read = "workflow.instances.read";
        public const string Cancel = "workflow.instances.cancel";
    }

    public static class Tasks
    {
        public const string Read = "workflow.tasks.read";
        public const string Complete = "workflow.tasks.complete";
        public const string Reassign = "workflow.tasks.reassign";
    }
}
