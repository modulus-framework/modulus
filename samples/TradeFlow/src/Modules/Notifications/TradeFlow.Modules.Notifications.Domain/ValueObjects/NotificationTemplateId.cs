namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

public readonly record struct NotificationTemplateId(Guid Value)
{
    public static NotificationTemplateId Create() => new(Guid.NewGuid());
    public static NotificationTemplateId From(Guid value) => new(value);
}
