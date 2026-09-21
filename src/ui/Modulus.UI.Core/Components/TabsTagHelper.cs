using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>One tab collected from an <c>&lt;m-tab&gt;</c> child.</summary>
/// <param name="PaneId">Unique pane element id (<c>{tabsId}-{tabId}</c>).</param>
/// <param name="Title">Tab title.</param>
/// <param name="Active">Selected on first render.</param>
/// <param name="Source">When set, the pane loads this URL with htmx the first time it is shown.</param>
/// <param name="Body">Pane markup (ignored when <paramref name="Source"/> is set).</param>
public sealed record TabModel(string PaneId, string Title, bool Active, string? Source, IHtmlContent Body);

/// <summary>View model for the <c>Tabs</c> component.</summary>
/// <param name="Id">Tabs element id.</param>
/// <param name="Tabs">The tabs, in order.</param>
public sealed record TabsModel(string Id, IReadOnlyList<TabModel> Tabs);

/// <summary>Collects <c>m-tab</c> children for their <c>m-tabs</c> parent.</summary>
internal sealed class TabsContext
{
    public List<TabModel> Tabs { get; } = [];
}

/// <summary>
/// <c>&lt;m-tabs&gt;&lt;m-tab id="general" title="General" active="true"&gt;…&lt;/m-tab&gt;&lt;/m-tabs&gt;</c> —
/// Tabler tabs driven by Bootstrap's data attributes (no page script). Give an <c>m-tab</c> a
/// <c>source</c> to lazy-load its pane with htmx when first shown. The first tab is selected when
/// none is marked active.
/// </summary>
[HtmlTargetElement("m-tabs")]
public sealed class TabsTagHelper : ComponentTagHelper
{
    /// <summary>Element id, used to namespace pane ids. Default <c>m-tabs</c>.</summary>
    [HtmlAttributeName("id")]
    public string Id { get; set; } = "m-tabs";

    /// <inheritdoc />
    protected override string Component => "Tabs";

    /// <inheritdoc />
    public override void Init(TagHelperContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[typeof(TabsContext)] = new TabsContext();
    }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        await output.GetChildContentAsync();
        var tabs = ((TabsContext)context.Items[typeof(TabsContext)]).Tabs;

        // Tab panes are named after this element's id, which is only known now.
        var named = tabs.Select(t => t with { PaneId = $"{Id}-{t.PaneId}" }).ToList();
        if (named.Count > 0 && named.All(t => !t.Active))
        {
            named[0] = named[0] with { Active = true };
        }

        await RenderAsync(output, new TabsModel(Id, named));
    }
}

/// <summary>A tab of an <c>m-tabs</c>; the element's content is the pane body.</summary>
[HtmlTargetElement("m-tab", ParentTag = "m-tabs")]
public sealed class TabTagHelper : TagHelper
{
    /// <summary>Tab id, unique within its <c>m-tabs</c> (required).</summary>
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    /// <summary>Tab title (HTML-encoded).</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Selected on first render.</summary>
    [HtmlAttributeName("active")]
    public bool Active { get; set; }

    /// <summary>URL loaded into the pane with htmx when it is first shown.</summary>
    [HtmlAttributeName("source")]
    public string? Source { get; set; }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("<m-tab> requires an id.");
        }

        var body = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(typeof(TabsContext), out var parent) && parent is TabsContext tabs)
        {
            tabs.Tabs.Add(new TabModel(Id, Title ?? Id, Active, Source, body));
        }

        output.SuppressOutput();
    }
}
