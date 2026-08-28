namespace ProcureFlow.Modules.Notifications.Domain.ValueObjects;

public readonly record struct NotificationRuleId(Guid Value)
{
    public static NotificationRuleId Create() => new(Guid.NewGuid());
    public static NotificationRuleId From(Guid value) => new(value);
}
