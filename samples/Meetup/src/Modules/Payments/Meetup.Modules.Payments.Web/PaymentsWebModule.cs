using Modulus.UI;

namespace Meetup.Modules.Payments.Web;

/// <summary>
/// Module-owned UI companion for the Payments module: contributes the
/// subscriptions admin page (<c>/Payments</c>) to the sidebar. Pages live
/// in this RCL (<c>Pages/Payments/</c>) and call the module's application
/// handlers in-process via the mediator.
/// </summary>
public sealed class PaymentsWebModule : CustomUiModule
{
    public PaymentsWebModule()
        : base(
            new ModuleManifest(
                "Meetup.Payments",
                "Payments",
                "1.0.0",
                [],
                ["Subscriptions"]),
            nav => nav
                .AddGroup("payments", "Payments", icon: "credit-card", order: 32)
                .AddItem(
                    "Payments.Subscriptions",
                    "Subscriptions",
                    "/Payments",
                    groupId: "payments"))
    {
    }
}
