namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

public readonly record struct NotificationLogId(Guid Value)
{
    public static NotificationLogId Create() => new(Guid.NewGuid());
    public static NotificationLogId From(Guid value) => new(value);
}
