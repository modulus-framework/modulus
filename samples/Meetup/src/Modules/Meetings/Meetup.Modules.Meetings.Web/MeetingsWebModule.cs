using Modulus.UI;

namespace Meetup.Modules.Meetings.Web;

/// <summary>
/// Module-owned UI companion for the Meetings module: contributes the
/// meetings admin page (<c>/Meetings</c>) to the sidebar. Groups and their
/// meetings are listed with an inline create form and per-row join.
/// Pages live in this RCL (<c>Pages/Meetings/</c>) and call the module's
/// application handlers in-process via the mediator.
/// </summary>
public sealed class MeetingsWebModule : CustomUiModule
{
    public MeetingsWebModule()
        : base(
            new ModuleManifest(
                "Meetup.Meetings",
                "Meetings",
                "1.0.0",
                [],
                ["Meetings"]),
            nav => nav
                .AddGroup("meetings", "Meetings", icon: "calendar", order: 30)
                .AddItem(
                    "Meetings.Meetings",
                    "Meetings",
                    "/Meetings",
                    groupId: "meetings"))
    {
    }
}
