using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>Card</c> component.</summary>
/// <param name="Title">Optional header title.</param>
/// <param name="Padded">Wrap the body in <c>card-body</c> (turn off for tables and lists).</param>
/// <param name="Body">Body markup.</param>
public sealed record CardModel(string? Title, bool Padded, IHtmlContent Body);

/// <summary>
/// <c>&lt;m-card title="..."&gt;body&lt;/m-card&gt;</c> — a Tabler card with an optional header.
/// Markup lives in the overridable <c>Card/Default</c> view.
/// </summary>
[HtmlTargetElement("m-card")]
public sealed class CardTagHelper : ComponentTagHelper
{
    /// <summary>Optional header title (HTML-encoded).</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Wrap the body in <c>card-body</c>. Default true; use false for tables and lists.</summary>
    [HtmlAttributeName("padded")]
    public bool Padded { get; set; } = true;

    /// <inheritdoc />
    protected override string Component => "Card";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = await output.GetChildContentAsync();
        await RenderAsync(output, new CardModel(Title, Padded, body));
    }
}
