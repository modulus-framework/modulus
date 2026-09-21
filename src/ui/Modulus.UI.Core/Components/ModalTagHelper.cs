using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>Modal</c> component.</summary>
/// <param name="Title">Header title.</param>
/// <param name="Dismissible">Show the header close button.</param>
/// <param name="Body">Body markup.</param>
/// <param name="Footer">Footer markup, or null for no footer.</param>
public sealed record ModalModel(string? Title, bool Dismissible, IHtmlContent Body, IHtmlContent? Footer);

/// <summary>Collects the <c>m-modal-footer</c> child for its <c>m-modal</c> parent.</summary>
internal sealed class ModalContext
{
    public IHtmlContent? Footer { get; set; }
}

/// <summary>
/// <c>&lt;m-modal title="New user"&gt;form&lt;/m-modal&gt;</c> — the content of the shared modal
/// (header, body, optional <c>&lt;m-modal-footer&gt;</c>). Return it as a fragment from a handler that a
/// link or button loads with <c>hx-get</c> into <c>#m-modal-container</c>; the theme runtime opens the
/// modal after the swap, and <c>Htmx.CloseModal()</c> closes it after a successful post.
/// </summary>
[HtmlTargetElement("m-modal")]
public sealed class ModalTagHelper : ComponentTagHelper
{
    /// <summary>Header title.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Show the header close button. Default true.</summary>
    [HtmlAttributeName("dismissible")]
    public bool Dismissible { get; set; } = true;

    /// <inheritdoc />
    protected override string Component => "Modal";

    /// <inheritdoc />
    public override void Init(TagHelperContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[typeof(ModalContext)] = new ModalContext();
    }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = await output.GetChildContentAsync();
        var modal = (ModalContext)context.Items[typeof(ModalContext)];
        await RenderAsync(output, new ModalModel(Title, Dismissible, body, modal.Footer));
    }
}

/// <summary>Footer of an <c>m-modal</c> (buttons); its content is captured, not rendered in place.</summary>
[HtmlTargetElement("m-modal-footer", ParentTag = "m-modal")]
public sealed class ModalFooterTagHelper : TagHelper
{
    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(typeof(ModalContext), out var parent) && parent is ModalContext modal)
        {
            modal.Footer = content;
        }

        output.SuppressOutput();
    }
}
