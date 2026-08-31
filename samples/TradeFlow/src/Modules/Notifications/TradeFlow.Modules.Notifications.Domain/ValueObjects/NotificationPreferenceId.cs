namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

public readonly record struct NotificationPreferenceId(Guid Value)
{
    public static NotificationPreferenceId Create() => new(Guid.NewGuid());
    public static NotificationPreferenceId From(Guid value) => new(value);
}
