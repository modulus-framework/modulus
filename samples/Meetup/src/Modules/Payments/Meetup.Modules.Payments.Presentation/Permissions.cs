namespace Meetup.Modules.Payments.Presentation;

/// <summary>Permission names for the Payments module (colon-style so the
/// framework's permission-policy provider enforces them server-side).</summary>
public static class PaymentsPermissions
{
    public const string BuySubscription = "payments:subscribe";
    public const string PayMeetingFee = "payments:fees:pay";
    public const string ViewPayments = "payments:view";
}
