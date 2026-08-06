namespace ModulusSample.Modules.Notifications.Domain.ValueObjects;

public readonly record struct NotificationId(Guid Value)
{
    public static NotificationId Create() => new(Guid.NewGuid());
    public static NotificationId From(Guid value) => new(value);
}