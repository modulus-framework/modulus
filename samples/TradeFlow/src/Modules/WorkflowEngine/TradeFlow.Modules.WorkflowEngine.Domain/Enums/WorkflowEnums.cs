namespace TradeFlow.Modules.WorkflowEngine.Domain.Enums;

public enum DefinitionStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2
}

public enum InstanceStatus
{
    Running = 0,
    Completed = 1,
    Rejected = 2,
    Cancelled = 3,
    TimedOut = 4
}

public enum TaskStatus
{
    Open = 0,
    Done = 1,
    Skipped = 2,
    Expired = 3,
    Reassigned = 4
}

public enum TaskDecision
{
    Approve = 0,
    Reject = 1,
    Return = 2,
    Reassign = 3,
    RequestInfo = 4
}

public enum StepType
{
    Approval = 0,
    ApprovalChain = 1,
    Parallel = 2,
    Condition = 3,
    SystemAction = 4,
    WaitEvent = 5,
    Timer = 6,
    SubWorkflow = 7,
    HumanTask = 8,
    Notification = 9
}

public enum AssignmentResolverType
{
    Position = 0,
    Role = 1,
    ManagerOf = 2,
    HeadOf = 3,
    DoA = 4,
    NamedUser = 5,
    RoundRobin = 6,
    LeastLoaded = 7
}
