using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Constants;

public static class NotificationErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Notification.NotFound", "Notification not found");

    public static readonly Error NotOwnedByUser =
        Error.Forbidden("Notification.NotOwnedByUser", "Notification does not belong to the current user");

    public static readonly Error RuleNotFound =
        Error.NotFound("NotificationRule.NotFound", "Notification rule not found");

    public static readonly Error RuleAlreadyExists =
        Error.Conflict("NotificationRule.AlreadyExists", "A notification rule already exists for this event key");

    public static readonly Error TemplateNotFound =
        Error.NotFound("NotificationTemplate.NotFound", "Notification template not found");

    public static readonly Error TemplateAlreadyExists =
        Error.Conflict("NotificationTemplate.AlreadyExists", "A notification template already exists for this key/channel/locale combination");

    public static readonly Error PreferenceNotFound =
        Error.NotFound("NotificationPreference.NotFound", "Notification preference not found");

    public static readonly Error PreferenceMandatory =
        Error.BusinessRule("NotificationPreference.Mandatory", "This notification category is mandatory and cannot be muted");

    public static readonly Error LogNotFound =
        Error.NotFound("NotificationLog.NotFound", "Notification log not found");

    public static readonly Error NoMatchingRules =
        Error.NotFound("NotificationEngine.NoMatchingRules", "No enabled notification rules matched the event");
}
