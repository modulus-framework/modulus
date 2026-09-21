using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>Stat</c> component.</summary>
/// <param name="Label">Small caption above the value.</param>
/// <param name="Value">The headline figure (already formatted).</param>
/// <param name="Delta">Optional change indicator (e.g. <c>+12%</c>).</param>
/// <param name="Tone">Colour of the delta: <c>success</c>, <c>danger</c>, <c>warning</c>, <c>secondary</c>, ...</param>
/// <param name="Hint">Optional muted footnote.</param>
public sealed record StatModel(string? Label, string? Value, string? Delta, string Tone, string? Hint);

/// <summary>
/// <c>&lt;m-stat label="Open orders" value="128" delta="+12%" tone="success" /&gt;</c> — a compact dashboard
/// tile. Values are passed already formatted, so culture and units stay with the page. Markup lives in
/// the overridable <c>Stat/Default</c> view.
/// </summary>
[HtmlTargetElement("m-stat")]
public sealed class StatTagHelper : ComponentTagHelper
{
    /// <summary>Caption above the value.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>The headline figure.</summary>
    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    /// <summary>Optional change indicator.</summary>
    [HtmlAttributeName("delta")]
    public string? Delta { get; set; }

    /// <summary>Colour of the delta. Default <c>secondary</c>.</summary>
    [HtmlAttributeName("tone")]
    public string Tone { get; set; } = "secondary";

    /// <summary>Optional muted footnote.</summary>
    [HtmlAttributeName("hint")]
    public string? Hint { get; set; }

    /// <inheritdoc />
    protected override string Component => "Stat";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => RenderAsync(output, new StatModel(Label, Value, Delta, Tone, Hint));
}
