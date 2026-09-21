using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>One entry of a <c>Timeline</c>.</summary>
/// <param name="Title">Headline of the event.</param>
/// <param name="Time">Already-formatted timestamp or relative time.</param>
/// <param name="Tone">Marker colour (<c>primary</c>, <c>success</c>, <c>danger</c>, ...).</param>
/// <param name="Body">Optional description markup.</param>
public sealed record TimelineItem(string? Title, string? Time, string Tone, IHtmlContent? Body);

/// <summary>View model for the <c>Timeline</c> component.</summary>
/// <param name="Items">The events, in the order written.</param>
public sealed record TimelineModel(IReadOnlyList<TimelineItem> Items);

/// <summary>Collects <c>m-timeline-item</c> children for their <c>m-timeline</c> parent.</summary>
internal sealed class TimelineContext
{
    public List<TimelineItem> Items { get; } = [];
}

/// <summary>
/// <c>&lt;m-timeline&gt;&lt;m-timeline-item title="Order shipped" time="2 h ago" tone="success" /&gt;&lt;/m-timeline&gt;</c> —
/// a vertical event history (audit trails, order progress). Items render in the order written.
/// Markup lives in the overridable <c>Timeline/Default</c> view.
/// </summary>
[HtmlTargetElement("m-timeline")]
public sealed class TimelineTagHelper : ComponentTagHelper
{
    /// <inheritdoc />
    protected override string Component => "Timeline";

    /// <inheritdoc />
    public override void Init(TagHelperContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[typeof(TimelineContext)] = new TimelineContext();
    }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        await output.GetChildContentAsync();
        var items = ((TimelineContext)context.Items[typeof(TimelineContext)]).Items;
        await RenderAsync(output, new TimelineModel(items));
    }
}

/// <summary>An event of an <c>m-timeline</c>; the element's content is the optional description.</summary>
[HtmlTargetElement("m-timeline-item", ParentTag = "m-timeline")]
public sealed class TimelineItemTagHelper : TagHelper
{
    /// <summary>Headline of the event.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Timestamp or relative time, already formatted.</summary>
    [HtmlAttributeName("time")]
    public string? Time { get; set; }

    /// <summary>Marker colour. Default <c>primary</c>.</summary>
    [HtmlAttributeName("tone")]
    public string Tone { get; set; } = "primary";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var child = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(typeof(TimelineContext), out var parent) && parent is TimelineContext timeline)
        {
            timeline.Items.Add(new TimelineItem(Title, Time, Tone, child.IsEmptyOrWhiteSpace ? null : child));
        }

        output.SuppressOutput();
    }
}
