using Modulus.UI;

namespace Meetup.Modules.UserAccess.Web;

/// <summary>
/// Module-owned UI companion for the UserAccess module: contributes the
/// users admin page (<c>/UserAccess</c>) to the sidebar. Pages live in this
/// RCL (<c>Pages/UserAccess/</c>) and call the module's application handlers
/// in-process via the mediator.
/// </summary>
public sealed class UserAccessWebModule : CustomUiModule
{
    public UserAccessWebModule()
        : base(
            new ModuleManifest(
                "Meetup.UserAccess",
                "UserAccess",
                "1.0.0",
                [],
                ["Users"]),
            nav => nav
                .AddGroup("useraccess", "User access", icon: "users", order: 33)
                .AddItem(
                    "UserAccess.Users",
                    "Users",
                    "/UserAccess",
                    groupId: "useraccess"))
    {
    }
}
