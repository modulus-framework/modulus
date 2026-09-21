namespace Modulus.UI.Theming;

/// <summary>
/// Named layout slots themes render so modules can inject content into the
/// shell without forking it (dashboards, notification bells, footer links).
/// Rendered via <c>Html.ModulusSlotAsync(...)</c>; contributions come from
/// <c>ISlotContributor</c> registrations, ordered and permission-filtered.
/// </summary>
public static class UiSlots
{
    /// <summary>Left side of the topbar (before search / actions).</summary>
    public const string TopbarStart = "Topbar.Start";

    /// <summary>Right side of the topbar (notifications, user menu).</summary>
    public const string TopbarEnd = "Topbar.End";

    /// <summary>Top of the sidebar, above the navigation.</summary>
    public const string SidebarTop = "Sidebar.Top";

    /// <summary>Bottom of the sidebar, below the navigation.</summary>
    public const string SidebarBottom = "Sidebar.Bottom";

    /// <summary>Above the page body content.</summary>
    public const string PageBeforeContent = "Page.BeforeContent";

    /// <summary>Below the page body content.</summary>
    public const string PageAfterContent = "Page.AfterContent";

    /// <summary>Inside the user dropdown in the topbar.</summary>
    public const string UserMenu = "UserMenu";

    /// <summary>Footer content area.</summary>
    public const string Footer = "Footer";

    /// <summary>End of <c>&lt;head&gt;</c> (meta tags, analytics — CSP applies).</summary>
    public const string Head = "Head";

    /// <summary>End of <c>&lt;body&gt;</c>, after the framework runtime scripts.</summary>
    public const string Scripts = "Scripts";

    /// <summary>Dashboard widget area (module dashboards compose here).</summary>
    public const string Dashboard = "Dashboard";

    /// <summary>All slot names (rendered slots depend on the layout variant).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        TopbarStart,
        TopbarEnd,
        SidebarTop,
        SidebarBottom,
        PageBeforeContent,
        PageAfterContent,
        UserMenu,
        Footer,
        Head,
        Scripts,
        Dashboard,
    ];
}
