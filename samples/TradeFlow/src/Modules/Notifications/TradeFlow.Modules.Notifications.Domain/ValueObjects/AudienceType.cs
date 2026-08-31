namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// How audience recipients are resolved from an event.
/// </summary>
public enum AudienceType
{
    Role = 0,
    Position = 1,
    SpecificUser = 2,
    DocumentParticipants = 3,
    ManagerOfInitiator = 4,
    HeadOfDepartment = 5,
    HeadOfBusinessUnit = 6,
    HeadOfCompany = 7
}
