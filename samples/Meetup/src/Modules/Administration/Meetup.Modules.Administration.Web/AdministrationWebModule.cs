using Modulus.UI;

namespace Meetup.Modules.Administration.Web;

/// <summary>
/// Module-owned UI companion for the Administration module: contributes the
/// proposals admin page (<c>/Administration</c>) to the sidebar. Pages live
/// in this RCL (<c>Pages/Administration/</c>) and call the module's
/// application handlers in-process via the mediator.
/// </summary>
public sealed class AdministrationWebModule : CustomUiModule
{
    public AdministrationWebModule()
        : base(
            new ModuleManifest(
                "Meetup.Administration",
                "Administration",
                "1.0.0",
                [],
                ["Proposals"]),
            nav => nav
                .AddGroup("administration", "Administration", icon: "shield", order: 34)
                .AddItem(
                    "Administration.Proposals",
                    "Proposals",
                    "/Administration",
                    groupId: "administration"))
    {
    }
}
