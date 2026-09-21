using Modulus.UI;

namespace Meetup.Modules.Registrations.Web;

/// <summary>
/// Module-owned UI companion for the Registrations module: contributes the
/// registrations admin page (<c>/Registrations</c>) to the sidebar. Pages
/// live in this RCL (<c>Pages/Registrations/</c>) and call the module's
/// application handlers in-process via the mediator.
/// </summary>
public sealed class RegistrationsWebModule : CustomUiModule
{
    public RegistrationsWebModule()
        : base(
            new ModuleManifest(
                "Meetup.Registrations",
                "Registrations",
                "1.0.0",
                [],
                ["Registrations"]),
            nav => nav
                .AddGroup("registrations", "Registrations", icon: "user-plus", order: 31)
                .AddItem(
                    "Registrations.Registrations",
                    "Registrations",
                    "/Registrations",
                    groupId: "registrations"))
    {
    }
}
